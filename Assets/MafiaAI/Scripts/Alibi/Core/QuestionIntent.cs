using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MafiaAI.Alibi
{
    public enum IntentTopic { Alibi, Victim, Opinion, Accuse, Other }

    /// <summary>
    /// 플레이어의 자유 질문을 엔진이 다룰 수 있는 형태로 바꾼 것.
    /// 무엇을 답할지는 엔진이 정하고, LLM은 그걸 말투로 옮기기만 한다.
    /// </summary>
    public sealed class QuestionIntent
    {
        public IntentTopic Topic = IntentTopic.Other;
        public List<int> Slots = new List<int>();
        public bool AllSlots;
        public int Relative;          // -1 그 전, +1 그 다음
        public bool AskLocation;
        public bool AskSeen;
        public string AboutPerson;    // "카이 봤어?"
        public string AboutRoom;      // "부엌에서 누구 봤어?"
        public string Source = "regex";

        public override string ToString() =>
            $"[{Source}] {Topic} slots={string.Join(",", Slots.Select(AlibiCase.TimeLabel))}{(AllSlots ? " ALL" : "")}" +
            $"{(Relative != 0 ? " rel=" + Relative : "")} where={AskLocation} who={AskSeen}" +
            $"{(AboutPerson != null ? " person=" + AboutPerson : "")}{(AboutRoom != null ? " room=" + AboutRoom : "")}";
    }

    /// <summary>한국어 문장에서 방·시각·사람을 찾는다. 질문 해석과 LLM 답 검증이 같은 규칙을 쓴다.</summary>
    public static class TextScan
    {
        static readonly (string Word, string Room)[] RoomWords =
        {
            ("현관홀", "현관홀"), ("현관", "현관홀"), ("로비", "현관홀"),
            ("응접실", "응접실"), ("거실", "응접실"),
            ("서재", "서재"),
            ("식당", "식당"), ("다이닝", "식당"),
            ("온실", "온실"),
            ("부엌", "부엌"), ("주방", "부엌"),
            ("세탁실", "세탁실"), ("빨래방", "세탁실"),
            ("창고", "창고"),
        };

        public static HashSet<string> Rooms(string text)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(text)) return set;
            foreach (var (w, r) in RoomWords) if (text.Contains(w)) set.Add(r);
            return set;
        }

        public static string CanonicalRoom(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return null;
            foreach (var (w, r) in RoomWords) if (word.Contains(w)) return r;
            return null;
        }

        public static HashSet<string> Persons(string text, string victim)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(text)) return set;
            foreach (var n in CharacterSheets.Names) if (text.Contains(n)) set.Add(n);
            if (victim != null && text.Contains("피해자")) set.Add(victim);
            return set;
        }

        static readonly Regex HourMin = new Regex(@"(\d{1,2})\s*(?:시|:)\s*(?:(\d{1,2})\s*분?|(반))?", RegexOptions.Compiled);

        /// <summary>문장 속 시각 표현 → 진술 슬롯. 슬롯에서 7분 넘게 떨어진 시각(예: 23:45 시체 발견)은 버린다.</summary>
        public static HashSet<int> Slots(string text)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrEmpty(text)) return set;
            foreach (Match m in HourMin.Matches(text))
            {
                if (!int.TryParse(m.Groups[1].Value, out int h)) continue;
                int min = 0;
                if (m.Groups[3].Success) min = 30;
                else if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out int mm)) min = mm;
                if (h <= 12) h += 12;           // 이 사건은 전부 밤이다: 11시 → 23시
                int slot = NearestSlot(h * 60 + min);
                if (slot >= 0) set.Add(slot);
            }
            if (text.Contains("비명") || text.Contains("사건 당시") || text.Contains("살인 당시") || text.Contains("죽던"))
                set.Add(AlibiCase.MurderSlot);
            return set;
        }

        public static int NearestSlot(int minuteOfDay)
        {
            int best = -1, bestGap = int.MaxValue;
            for (int i = 0; i < AlibiCase.SlotTimes.Length; i++)
            {
                var p = AlibiCase.SlotTimes[i].Split(':');
                int m = int.Parse(p[0]) * 60 + int.Parse(p[1]);
                int gap = Math.Abs(m - minuteOfDay);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return bestGap <= 7 ? best : -1;
        }

        public static bool Any(string text, params string[] words)
        {
            foreach (var w in words) if (text.Contains(w)) return true;
            return false;
        }
    }

    /// <summary>규칙 기반 해석(LLM 없이도 게임이 돌아가게 — 'AI 없이 재밌나' 비교 모드의 기반).</summary>
    public static class IntentParser
    {
        static readonly string[] SeenCues = { "누구", "누굴", "봤", "보았", "본 사람", "마주", "만났", "만난", "같이 있", "함께 있", "혼자", "목격" };
        static readonly string[] WhereCues = { "어디", "있었", "뭐 했", "뭐했", "무엇을 했", "갔", "동선", "알리바이", "위치" };
        static readonly string[] AllCues = { "밤새", "내내", "전부", "다 말", "처음부터", "동선", "하나씩", "시간대별", "시간별", "순서대로" };
        static readonly string[] NextCues = { "그 다음", "그다음", "그 뒤", "그뒤", "그 후", "그후", "이후엔", "다음엔" };
        static readonly string[] PrevCues = { "그 전", "그전", "그 앞", "그보다 전", "이전", "전에는", "그 직전" };
        static readonly string[] AccuseCues = { "범인이지", "범인이잖", "죽였지", "죽였잖", "네가 범인", "니가 범인", "당신이 범인", "네가 죽", "니가 죽", "당신이 죽", "자백" };
        static readonly string[] OpinionCues = { "누가 범인", "누가 죽", "의심", "수상", "누가 그랬", "범인 같", "어떻게 생각" };
        static readonly string[] VictimCues = { "사이", "관계", "어떤 사람", "친했", "원한", "싫어", "좋아", "알던", "아는 사이" };

        public static QuestionIntent ParseRegex(string text, AlibiCase c)
        {
            var it = new QuestionIntent { Source = "regex" };
            text = text ?? "";
            var slots = TextScan.Slots(text);
            var persons = TextScan.Persons(text, c.Victim);
            var rooms = TextScan.Rooms(text);

            it.Slots = slots.OrderBy(x => x).ToList();
            it.AllSlots = TextScan.Any(text, AllCues);
            it.Relative = TextScan.Any(text, NextCues) ? 1 : TextScan.Any(text, PrevCues) ? -1 : 0;
            it.AskSeen = TextScan.Any(text, SeenCues);
            it.AskLocation = TextScan.Any(text, WhereCues);
            it.AboutPerson = persons.FirstOrDefault();
            it.AboutRoom = rooms.FirstOrDefault();

            bool alibiCue = it.AskSeen || it.AskLocation || it.Slots.Count > 0 || it.AllSlots || it.Relative != 0;
            if (alibiCue) it.Topic = IntentTopic.Alibi;
            else if (TextScan.Any(text, AccuseCues)) it.Topic = IntentTopic.Accuse;
            else if ((persons.Contains(c.Victim) || text.Contains("피해자")) && TextScan.Any(text, VictimCues)) it.Topic = IntentTopic.Victim;
            else if (TextScan.Any(text, OpinionCues)) it.Topic = IntentTopic.Opinion;
            else if (persons.Contains(c.Victim) || text.Contains("피해자")) it.Topic = IntentTopic.Victim;
            return it;
        }

        /// <summary>LLM JSON → 의도. 실패하면 null(호출 측이 규칙 해석으로 대체).</summary>
        public static QuestionIntent FromJson(string json, AlibiCase c)
        {
            var o = MiniJson.ParseObject(json);
            if (o == null) return null;
            var it = new QuestionIntent { Source = "llm" };
            switch ((MiniJson.Str(o, "topic") ?? "").ToLowerInvariant())
            {
                case "alibi": it.Topic = IntentTopic.Alibi; break;
                case "victim": it.Topic = IntentTopic.Victim; break;
                case "opinion": it.Topic = IntentTopic.Opinion; break;
                case "accuse": it.Topic = IntentTopic.Accuse; break;
                default: it.Topic = IntentTopic.Other; break;
            }
            foreach (var t in MiniJson.StrList(o, "times"))
                foreach (var s in TextScan.Slots(t)) if (!it.Slots.Contains(s)) it.Slots.Add(s);
            it.Slots.Sort();
            it.AllSlots = MiniJson.Bool(o, "all_times");
            var rel = (MiniJson.Str(o, "relative") ?? "").ToLowerInvariant();
            it.Relative = rel == "next" ? 1 : rel == "prev" ? -1 : 0;
            it.AskLocation = MiniJson.Bool(o, "ask_where");
            it.AskSeen = MiniJson.Bool(o, "ask_who");
            var person = MiniJson.Str(o, "person");
            if (!string.IsNullOrEmpty(person))
            {
                var p = TextScan.Persons(person, c.Victim).FirstOrDefault();
                it.AboutPerson = p;
            }
            it.AboutRoom = TextScan.CanonicalRoom(MiniJson.Str(o, "room"));
            return it;
        }

        /// <summary>
        /// LLM 해석을 기본으로 쓰되, 규칙이 확실히 잡은 것(명시적 시각, 같은 방 이름)은 보탠다.
        /// 작은 모델이 시각을 빠뜨리는 실수를 규칙이 메운다.
        /// </summary>
        public static QuestionIntent Merge(QuestionIntent llm, QuestionIntent rx)
        {
            if (llm == null) return rx;
            foreach (var s in rx.Slots) if (!llm.Slots.Contains(s)) llm.Slots.Add(s);
            llm.Slots.Sort();
            if (rx.Slots.Count > 0 && llm.Topic != IntentTopic.Alibi && (rx.AskSeen || rx.AskLocation))
                llm.Topic = IntentTopic.Alibi;
            if (llm.Topic == IntentTopic.Alibi)
            {
                if (llm.AboutRoom == null) llm.AboutRoom = rx.AboutRoom;
                if (llm.AboutPerson == null && rx.AskSeen) llm.AboutPerson = rx.AboutPerson;
                // '누구/봤/혼자' 같은 단서는 규칙이 더 확실하다 — 놓친 쪽을 보탠다(과하게 켜져도 해는 없다)
                llm.AskSeen |= rx.AskSeen;
                llm.AskLocation |= rx.AskLocation;
                if (llm.Relative == 0) llm.Relative = rx.Relative;
                llm.AllSlots |= rx.AllSlots;
            }
            llm.Source = "llm+regex";
            return llm;
        }
    }
}
