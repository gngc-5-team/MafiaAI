using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MafiaAI.Alibi
{
    /// <summary>LLM 호출 하나(시스템 + 사용자 프롬프트).</summary>
    public struct PromptPair
    {
        public string System;
        public string User;
        public PromptPair(string system, string user) { System = system; User = user; }
    }

    /// <summary>
    /// 엔진이 정한 '이번 대답'. LLM은 FactLines만 말투로 옮긴다.
    /// 답이 사실을 벗어났는지는 Allowed/Must 집합으로 코드가 검사한다.
    /// </summary>
    public sealed class AnswerPlan
    {
        public readonly List<string> FactLines = new List<string>();
        public readonly List<CellRef> RevealLocation = new List<CellRef>();
        public readonly List<CellRef> RevealSeen = new List<CellRef>();
        public readonly HashSet<string> AllowedRooms = new HashSet<string>();
        public readonly HashSet<int> AllowedSlots = new HashSet<int>();
        public readonly HashSet<string> AllowedPersons = new HashSet<string>();
        public readonly HashSet<string> MustRooms = new HashSet<string>();
        public readonly HashSet<string> MustPersons = new HashSet<string>();
        public bool NoPlaceTime;    // 의견·잡담·발뺌: 장소·시각을 새로 말하면 안 된다
        public string Stance;
        public string Fallback;     // 검증 실패 시 쓸 안전한 문장
        public int FocusSlot = -1;  // 다음 "그 다음엔?"의 기준 시각
        public int Sentences = 2;   // 대사 문장 상한(여러 시각을 말할 땐 시각 수만큼)
        public int MaxTokens;       // 0이면 기본값
    }

    public static class AlibiPrompts
    {
        // ───────────── 질문 해석(JSON) ─────────────

        public static PromptPair Intent(AlibiCase c, string suspect, string question, string lastQ, string lastA, int lastSlot)
        {
            const string sys = "너는 추리 게임의 질문 분석기다. 탐정이 용의자에게 한 질문을 JSON 하나로만 분류한다. 설명은 쓰지 않는다.";
            var sb = new StringBuilder();
            sb.AppendLine("[저택의 방] " + string.Join(", ", AlibiMap.Rooms.Select(r => r.Name)));
            sb.AppendLine("[사람] " + string.Join(", ", CharacterSheets.Names) + " (피해자: " + c.Victim + ")");
            sb.AppendLine("[진술 시각] 22:45, 23:00, 23:15(비명), 23:30");
            sb.AppendLine("[질문 받는 사람] " + suspect);
            if (!string.IsNullOrEmpty(lastQ))
                sb.AppendLine("[직전 대화] 탐정: \"" + lastQ + "\" / " + suspect + ": \"" + lastA + "\"");
            if (lastSlot >= 0) sb.AppendLine("[직전에 다룬 시각] " + AlibiCase.TimeLabel(lastSlot));
            sb.AppendLine("[탐정의 질문] \"" + question + "\"");
            sb.AppendLine();
            sb.AppendLine("아래 형식의 JSON 하나로만 답하라.");
            sb.AppendLine("{\"topic\":\"alibi\",\"times\":[\"23:15\"],\"all_times\":false,\"relative\":\"none\",\"ask_where\":true,\"ask_who\":false,\"person\":\"\",\"room\":\"\"}");
            sb.AppendLine("- topic: 어디 있었는지·누구를 봤는지 묻는 질문은 alibi. 피해자와의 관계는 victim. 누가 범인 같냐는 opinion. 너가 범인이지? 같은 추궁은 accuse. 그 밖은 other.");
            sb.AppendLine("- times: 질문에 나온 시각을 진술 시각으로 바꿔 넣는다(11시 15분 → 23:15, 비명 들렸을 때 → 23:15). 없으면 [].");
            sb.AppendLine("- all_times: 밤새 동선 전체를 물으면 true.");
            sb.AppendLine("- relative: '그 다음/그 뒤'는 next, '그 전'은 prev, 아니면 none.");
            sb.AppendLine("- ask_where: 어디 있었는지 물으면 true. ask_who: 누구를 봤는지·누구와 있었는지·혼자였는지 물으면 true.");
            sb.AppendLine("- person: 특정 인물을 봤는지 묻는다면 그 이름. room: 특정 방에서의 일을 묻는다면 그 방 이름. 없으면 \"\".");
            sb.AppendLine("예) \"11시 15분에 어디 있었어?\" → {\"topic\":\"alibi\",\"times\":[\"23:15\"],\"all_times\":false,\"relative\":\"none\",\"ask_where\":true,\"ask_who\":false,\"person\":\"\",\"room\":\"\"}");
            sb.AppendLine("예) \"부엌에서 누구 봤어?\" → {\"topic\":\"alibi\",\"times\":[],\"all_times\":false,\"relative\":\"none\",\"ask_where\":false,\"ask_who\":true,\"person\":\"\",\"room\":\"부엌\"}");
            sb.AppendLine("예) \"그 다음엔 누구랑 있었어?\" → {\"topic\":\"alibi\",\"times\":[],\"all_times\":false,\"relative\":\"next\",\"ask_where\":true,\"ask_who\":true,\"person\":\"\",\"room\":\"\"}");
            sb.AppendLine("예) \"피해자랑은 무슨 사이였어?\" → {\"topic\":\"victim\",\"times\":[],\"all_times\":false,\"relative\":\"none\",\"ask_where\":false,\"ask_who\":false,\"person\":\"\",\"room\":\"\"}");
            return new PromptPair(sys, sb.ToString());
        }

        // ───────────── 대답(말투) ─────────────

        public static string SystemPrompt(CharacterSheet who)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"너는 추리 게임 속 인물 '{who.Name}'이다. 저택 살인 사건 때문에 탐정에게 심문받고 있다.");
            sb.AppendLine("[말투] " + who.Style);
            sb.AppendLine("[말투 예시] " + who.Examples);
            sb.AppendLine("[규칙]");
            sb.AppendLine("- 한국어 대사로 짧게 말한다(보통 1~2문장). 지문·괄호·따옴표·이모지·이름표 없이 말만 한다.");
            sb.AppendLine("- 자기 이름으로 자신을 부르지 않는다.");
            sb.AppendLine("- 시각은 '23시 15분'처럼 숫자로 말한다.");
            sb.AppendLine("- [말할 내용]에 없는 장소·시각·사람 이야기는 절대 지어내지 않는다. 모르는 건 모른다고 한다.");
            sb.AppendLine($"- 탐정은 '{who.CallsDetective}'라고 부른다.");
            return sb.ToString();
        }

        public static PromptPair Reply(AlibiCase c, Suspect p, string question, IReadOnlyList<(string Who, string Text)> history, AnswerPlan plan)
        {
            var who = CharacterSheets.Get(p.Name);
            var sb = new StringBuilder();
            sb.AppendLine(CaseLine(c));
            sb.AppendLine("[너와 피해자] " + p.Relation);
            if (history != null && history.Count > 0)
            {
                sb.AppendLine("[지금까지 대화]");
                foreach (var (w, t) in history.Skip(Math.Max(0, history.Count - 6)))
                    sb.AppendLine((w == p.Name ? "너" : w) + ": " + t);
            }
            sb.AppendLine("[탐정의 질문] " + question);
            sb.AppendLine("[말할 내용]");
            foreach (var f in plan.FactLines) sb.AppendLine("- " + f);
            if (plan.NoPlaceTime) sb.AppendLine("- 장소와 시각은 말하지 않는다.");
            if (plan.Sentences > 2) sb.AppendLine("- 여러 시각을 말할 때는 시각 순서대로, 하나도 빼지 않고 짧게 말한다.");
            sb.AppendLine("[태도] " + plan.Stance);
            sb.AppendLine("[말할 내용]을 빠짐없이 네 말투로 답하라.");
            return new PromptPair(SystemPrompt(who), sb.ToString());
        }

        public static string CaseLine(AlibiCase c) =>
            $"[사건] 23시 15분에 비명이 들렸고, {Josa.IGa(c.Victim)} {c.MurderRoom}에서 숨진 채 발견됐다. 흉기는 {c.WeaponRoom}에 있던 {c.Weapon}.";

        public static string Correction(string problem) =>
            $"\n[주의] 방금 답은 규칙을 어겼다: {problem}. [말할 내용]을 빠짐없이, 그것만 다시 답하라.";

        // ───────────── 검증 ─────────────

        /// <summary>LLM 답이 엔진이 허락한 사실만 말했는지. 문제가 없으면 null.</summary>
        public static string Validate(string reply, AnswerPlan plan, string speaker, string question, AlibiCase c)
        {
            if (string.IsNullOrWhiteSpace(reply)) return "빈 답";
            var qRooms = TextScan.Rooms(question);
            var qSlots = TextScan.Slots(question);
            var qPersons = TextScan.Persons(question, c.Victim);

            foreach (var r in TextScan.Rooms(reply))
                if (!plan.AllowedRooms.Contains(r) && !qRooms.Contains(r))
                    return $"사실에 없는 장소 '{r}'를 말했다";
            foreach (var s in TextScan.Slots(reply))
                if (!plan.AllowedSlots.Contains(s) && !qSlots.Contains(s) && !(s == AlibiCase.MurderSlot && !plan.NoPlaceTime))
                    return $"사실에 없는 시각 '{AlibiCase.TimeLabel(s)}'를 말했다";
            if (!plan.NoPlaceTime)
            {
                foreach (var n in TextScan.Persons(reply, null))
                {
                    if (n == speaker) return "자기 이름을 말했다";
                    if (n != c.Victim && !plan.AllowedPersons.Contains(n) && !qPersons.Contains(n))
                        return $"사실에 없는 인물 '{n}'을 말했다";
                }
            }
            else if (reply.Contains(speaker)) return "자기 이름을 말했다";

            foreach (var r in plan.MustRooms)
                if (!TextScan.Rooms(reply).Contains(r)) return $"꼭 말해야 할 장소가 빠졌다: {r}";
            foreach (var n in plan.MustPersons)
                if (n != c.Victim && !reply.Contains(n)) return $"봤다는 사람이 빠졌다: {n}";
            return null;
        }

        static readonly Regex Paren = new Regex(@"\([^)]*\)|\[[^\]]*\]|\*[^*]*\*", RegexOptions.Compiled);

        /// <summary>이름표·따옴표·지문 제거 + 문장 수 컷(기본 2문장, 동선 전체를 말할 땐 3문장).</summary>
        public static string Clean(string raw, string speaker, int maxSentences = 2)
        {
            if (raw == null) return "";
            var s = raw.Trim();
            foreach (var prefix in new[] { speaker + ":", speaker + " :", "너:", "나:" })
                if (s.StartsWith(prefix)) s = s.Substring(prefix.Length).Trim();
            s = Paren.Replace(s, "");
            s = s.Replace("\"", "").Replace("“", "").Replace("”", "").Replace("'", "").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");

            // 2문장까지(…, ?, !, . 기준). 너무 길면 자른다
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '.' || ch == '?' || ch == '!' || ch == '…')
                {
                    while (i + 1 < s.Length && (s[i + 1] == '.' || s[i + 1] == '?' || s[i + 1] == '!' || s[i + 1] == '…')) i++;
                    count++;
                    if (count == maxSentences && i + 1 < s.Length) { s = s.Substring(0, i + 1); break; }
                }
            }
            int cap = 90 * maxSentences;
            if (s.Length > cap) s = s.Substring(0, cap).TrimEnd() + "…";
            return s;
        }
    }
}
