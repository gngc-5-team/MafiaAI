using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MafiaAI.Alibi;

namespace MafiaAI.Mystery
{
    /// <summary>
    /// 자유 질문(직접 입력) — 그 인물이 '사람처럼' 대답하되, 사건을 망가뜨리지 않게.
    ///
    /// 주는 것: 성격·내력·다른 손님에 대한 인상(Persona) · 오늘 밤 확실히 아는 사실 · 한 말(지킬 거짓 포함) · 들켜서 인정한 사실 ·
    ///          아직 숨기는 것의 '태도와 넌지시 가리키는 곳'(Hint — 내용은 넣지 않는다) · 이 사람과 나눈 최근 대화.
    /// 막는 것: 모르는 시각·방을 지어내면 버린다 · 숨기는 내용이 새어 나오면(Guard) 버린다 · 너무 짧은 얼버무림은 한 번 더 묻는다.
    /// 두 번 다 안 되면 그 사람 말투의 얼버무림으로 대신한다.
    /// </summary>
    public static class MysteryTalk
    {
        static readonly Regex TimeRx = new Regex(@"\d{1,2}\s*시(\s*\d{1,2}\s*분)?|\d{1,2}:\d{2}");
        static readonly string[] RoomWords = { "서재", "응접실", "식당", "온실", "현관홀", "부엌", "세탁실", "창고" };
        public const int HistoryLines = 10;

        public static string Knowledge(MysterySession s, string name)
        {
            var c = s.Case;
            var who = c.SuspectByName(name);
            var sb = new StringBuilder();
            sb.AppendLine("[모두가 아는 일] 오늘 밤 안개곶 저택 만찬에서 소설가 노아가 새 소설 『가면들의 만찬』이 이 자리 손님들의 이야기라고 밝혔다. 23시 30분 하루가 서재에서 숨진 노아를 발견했다. 폭풍으로 바닷길이 잠기고 전화가 끊겨 새벽까지 아무도 저택을 나갈 수 없다. 탐정은 23시 40분에 도착했다.");
            sb.AppendLine("[오늘 밤 네가 확실히 아는 일] " + who.Facts);
            foreach (var st in s.Statements(name))
            {
                if (s.Broken(st)) sb.AppendLine("[탐정에게 들켜서 인정한 사실] " + (st.Truth ?? st.Text));
                else if (st.Lie) sb.AppendLine("[네가 탐정에게 한 말 — 증거로 무너지기 전까지 이 말을 지킨다] " + st.Text);
                else sb.AppendLine("[네가 한 말] " + st.Text);
            }
            foreach (var t in s.Topics(name).Where(s.Asked))
                sb.AppendLine($"[이미 대답한 것: {t.Label}] " + string.Join(" ", t.Lines));
            if (s.IsDead("세이") && name != "세이") sb.AppendLine("[새로 일어난 일] 세이가 온실에서 숨진 채 발견됐다.");
            return sb.ToString();
        }

        /// <summary>아직 들키지 않은 거짓 — 태도와 가리키는 곳만(내용은 넣지 않는다).</summary>
        static IEnumerable<Statement> Hidden(MysterySession s, string name) =>
            s.Case.SuspectByName(name).Statements.Where(st => st.Lie && !s.Broken(st));

        public static string SystemPrompt(MysterySession s, string name)
        {
            var sheet = CharacterSheets.Get(name);
            var who = s.Case.SuspectByName(name);
            var hints = Hidden(s, name).Where(st => !string.IsNullOrEmpty(st.Hint)).Select(st => "- " + st.Hint).ToList();
            return
$@"너는 추리 게임 「자정의 원고」 속 인물 '{name}'({who.Role})이다. 폭풍에 갇힌 안개곶 저택에서 탐정과 마주 서서 이야기하고 있다.
말투: {sheet.Style}
말투 예시: {sheet.Examples}
탐정을 부르는 말: {sheet.CallsDetective}

[너는 누구인가]
{who.Persona ?? who.Bio}

{Knowledge(s, name)}
[속에 감춘 것 — 무엇을 감추는지는 절대 말하지 않는다. 이렇게 행동할 뿐이다]
{(hints.Count > 0 ? string.Join("\n", hints) : "- 없음")}

대화 규칙:
- 탐정의 마지막 말에 직접 대답한다. 바로 앞 대화를 이어받아, 무엇을 묻는지 알아듣고 답한다.
- 감정·생각·인상, 노아와의 추억, 다른 손님에 대한 의견, 저택과 폭풍 이야기는 위 [너는 누구인가]에 맞게 자유롭게 말해도 된다.
- 사건 사실(시각·장소·누가 무엇을 했는지·물건)은 위에 적힌 것만 말한다. 적혀 있지 않으면 모른다고 하거나 그렇게 말할 이유를 네 성격대로 댄다. 새 사건을 지어내지 않는다.
- 감춘 것에 가까운 질문엔 얼버무리되, '왜 말하기 싫은지'가 드러나게 한다(위 행동대로).
- 다른 사람을 범인이라고 단정하지 않는다.
- 1~3문장, 한국어 대사만 쓴다(따옴표·지문·이름표 없이).";
        }

        /// <summary>이 사람과 나눈 최근 대화(인사 · 질문 쪽지 · 증거 보여 주기 · 직접 묻기) — 이어지는 질문을 알아듣게.</summary>
        public static List<Line> History(MysterySession s, string name, int max = HistoryLines)
        {
            var lines = new List<Line>();
            var log = s.Log;
            for (int i = 0; i < log.Count; i++)
            {
                var l = log[i];
                if (l.Speaker == name) lines.Add(l);
                else if (l.Speaker == "탐정" && i + 1 < log.Count && log[i + 1].Speaker == name) lines.Add(l);
            }
            return lines.Skip(Math.Max(0, lines.Count - max)).ToList();
        }

        public static string Prompt(MysterySession s, string name, string question)
        {
            var sb = new StringBuilder();
            var history = History(s, name);
            if (history.Count > 0)
            {
                sb.AppendLine("[지금까지 나눈 대화]");
                foreach (var l in history) sb.AppendLine($"{l.Speaker}: {l.Text}");
                sb.AppendLine();
            }
            sb.AppendLine("탐정: " + question.Trim());
            sb.Append(name + ":");
            return sb.ToString();
        }

        public static async Task<string> AnswerAsync(ILlm llm, MysterySession s, string name, string question, CancellationToken ct)
        {
            if (llm == null) return Deflect(name);
            string system = SystemPrompt(s, name);
            string knowledge = Knowledge(s, name) + "\n" + string.Join("\n", History(s, name).Select(l => l.Text)) + "\n" + question;
            var guards = Hidden(s, name).SelectMany(st => st.Guard ?? Array.Empty<string>()).ToArray();
            string prompt = Prompt(s, name, question);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string raw;
                try { raw = await llm.GenerateAsync(system, prompt, attempt == 0 ? 0.6f : 0.8f, false, 170, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { return Deflect(name); }
                var answer = Clean(raw, name);
                var problem = Validate(answer, knowledge, guards);
                if (problem == null) return answer;
                if (problem == "너무 짧음" && attempt == 2) return answer;
            }
            return Deflect(name);
        }

        /// <summary>문제가 없으면 null. 사실에 없는 시각이나 방을 꺼내거나, 숨기는 것이 새어 나오면 거절한다.</summary>
        public static string Validate(string answer, string knowledge, string[] guards = null)
        {
            if (string.IsNullOrWhiteSpace(answer)) return "빈 답";
            if (Regex.IsMatch(answer, "[A-Za-z]{4,}")) return "영어";
            string flatKnowledge = Regex.Replace(knowledge, @"\s", "");
            foreach (Match m in TimeRx.Matches(answer))
            {
                string t = Regex.Replace(m.Value, @"\s", "");
                if (!flatKnowledge.Contains(t)) return "모르는 시각 " + m.Value;
            }
            foreach (var room in RoomWords)
                if (answer.Contains(room) && !knowledge.Contains(room)) return "모르는 방 " + room;
            if (guards != null)
                foreach (var g in guards)
                    if (!string.IsNullOrEmpty(g) && answer.Contains(g)) return "새어 나옴 " + g;
            if (Regex.Matches(answer, "[가-힣]").Count < 8) return "너무 짧음";
            return null;
        }

        public static string Clean(string raw, string name)
        {
            if (raw == null) return "";
            var t = raw.Trim().Trim('"', '“', '”', '\'');
            if (t.StartsWith(name + ":")) t = t.Substring(name.Length + 1).Trim();
            int cut = t.IndexOf("\n탐정", StringComparison.Ordinal);       // 혼자 다음 질문까지 쓰지 않게
            if (cut > 0) t = t.Substring(0, cut);
            t = t.Replace("\n", " ");
            t = Regex.Replace(t, @"\([^)]*\)|\*[^*]*\*", "").Trim();
            var sentences = Regex.Split(t, @"(?<=[.!?…~])\s+").Where(x => x.Trim().Length > 0).Take(3);
            t = string.Join(" ", sentences).Trim();
            if (t.Length > 200) t = t.Substring(0, 200).TrimEnd() + "…";
            return t;
        }

        public static string Deflect(string name)
        {
            switch (CharacterSheets.Get(name).Speech)
            {
                case Speech.Hapsyo: return "그 질문에는 지금 드릴 말씀이 없습니다. 다른 걸 물어보시죠.";
                case Speech.Haeyo: return "그, 그건… 지금은 제가 말씀드리기가 좀… 다른 걸 물어봐 주세요.";
                default: return name == "세이" ? "…그건 말하기 싫어." : "그건 몰라. 다른 걸 물어.";
            }
        }
    }
}
