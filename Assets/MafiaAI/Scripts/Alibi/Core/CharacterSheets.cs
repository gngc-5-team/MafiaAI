using System;
using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Alibi
{
    public enum Speech { Banmal, Haeyo, Hapsyo }

    /// <summary>
    /// 인물 시트 — 말투만 담는다. 마피아판 PersonaLibrary(트롤·감몰이 같은 '플레이 성향')는 쓰지 않는다:
    /// 성향이 섞이면 무고한 사람도 수상해 보여 통제 불가 거짓 양성이 된다(2026-08-01 교훈).
    /// </summary>
    public sealed class CharacterSheet
    {
        public string Name;
        public string PortraitKey;  // Assets/sprites/UI/Char/{key}.png
        public Speech Speech;
        public string Style;        // SYSTEM 말투 지시
        public string Examples;     // 말투 예시(알리바이 상황)
        public string CallsDetective;
        public string Tagline;      // 심문 창에 보일 한 줄 인상

        public CharacterSheet(string name, string key, Speech speech, string callsDetective, string tagline, string style, string examples)
        {
            Name = name; PortraitKey = key; Speech = speech; CallsDetective = callsDetective; Tagline = tagline; Style = style; Examples = examples;
        }
    }

    public static class CharacterSheets
    {
        public static readonly CharacterSheet[] All =
        {
            new CharacterSheet("카이", "kai", Speech.Banmal, "탐정", "퉁명스러운 다혈질",
                "거친 반말. 짧고 퉁명스럽다. 질문받는 걸 귀찮아하며 쏘아붙이지만 묻는 말에는 답한다.",
                "\"부엌에 있었어. 됐냐?\" / \"몇 번을 말해. 아무도 못 봤다고.\""),
            new CharacterSheet("제로", "zero", Speech.Hapsyo, "탐정님", "냉정한 원칙주의자",
                "차가운 존댓말. 군더더기 없이 정확하게, 시각과 장소를 또박또박 말한다.",
                "\"23시에는 서재에 있었습니다. 그 시각 서재에는 저 혼자였습니다.\" / \"사실만 말씀드리죠.\""),
            new CharacterSheet("미로", "miro", Speech.Banmal, "탐정님", "능글맞은 장난꾼",
                "능글맞은 반말. 농담을 섞고 탐정을 놀리듯 말하지만 묻는 말에는 답한다.",
                "\"온실? 응, 거기 있었지~ 꽃이 예쁘더라고.\" / \"에이, 탐정님 너무 무섭게 물어보신다.\""),
            new CharacterSheet("하루", "haru", Speech.Haeyo, "탐정님", "불안한 감성파",
                "불안하고 감정적인 말투. 존댓말을 쓰다가 흥분하면 반말이 섞이고, 말끝을 흐린다.",
                "\"저, 저는 식당에 있었어요… 진짜예요.\" / \"누굴 봤냐고요? 아무도… 아무도 없었어요.\""),
            new CharacterSheet("노아", "noa", Speech.Hapsyo, "탐정님", "극적인 몽상가",
                "과몰입한 존댓말. 비장하고 극적으로 말하지만 길게 늘어놓지는 않는다.",
                "\"그 시각, 저는 응접실에 있었습니다. 운명처럼 말이죠.\" / \"제가 본 것은 오직 하루 씨뿐이었습니다.\""),
            new CharacterSheet("세이", "sei", Speech.Banmal, "탐정", "무심한 관찰자",
                "무심한 반말 단문. 조사를 자주 생략하고 감정 표현이 거의 없다.",
                "\"세탁실. 혼자.\" / \"몰라. 아무도 못 봤어.\""),
        };

        public static CharacterSheet Get(string name) => All.First(c => c.Name == name);
        public static IEnumerable<string> Names => All.Select(c => c.Name);

        static readonly string[] Relations =
        {
            "오랜 친구", "사업 파트너", "옛 연인", "큰돈을 빌린 사이", "먼 친척",
            "비서로 일했던 사이", "오늘 처음 본 사이", "대학 동기",
        };
        static readonly string[] Feelings =
        {
            "서운한 게 많았다", "고마운 사람이었다", "솔직히 불편했다", "별 감정은 없었다", "최근 크게 다퉜다",
        };

        public static string RandomRelation(Random rng, string victim) =>
            $"{Josa.WaGwa(victim)}는 {Relations[rng.Next(Relations.Length)]}. {Feelings[rng.Next(Feelings.Length)]}.";

        // ── 템플릿 대사(LLM을 끄거나 LLM 답이 사실을 어겼을 때 쓰는 안전한 문장) ──

        public static string SayLocation(Speech s, int slot, string room)
        {
            string t = AlibiCase.SpokenTime(slot);
            switch (s)
            {
                case Speech.Hapsyo: return $"{t}에는 {room}에 있었습니다.";
                case Speech.Haeyo: return $"{t}에는 {room}에 있었어요.";
                default: return $"{t}엔 {room}에 있었어.";
            }
        }

        public static string SaySeen(Speech s, IReadOnlyList<string> seen, string victim)
        {
            if (seen == null || seen.Count == 0)
            {
                switch (s)
                {
                    case Speech.Hapsyo: return "그곳에서 아무도 보지 못했습니다.";
                    case Speech.Haeyo: return "거기서 아무도 못 봤어요.";
                    default: return "거기서 아무도 못 봤어.";
                }
            }
            var names = string.Join(", ", seen.Select(n => n == victim ? n + "(피해자)" : n));
            switch (s)
            {
                case Speech.Hapsyo: return $"그곳에서 {Josa.EulReul(names)} 봤습니다.";
                case Speech.Haeyo: return $"거기서 {Josa.EulReul(names)} 봤어요.";
                default: return $"거기서 {Josa.EulReul(names)} 봤어.";
            }
        }

        public static string AskForTime(Speech s)
        {
            switch (s)
            {
                case Speech.Hapsyo: return "누구를 봤는지는 시각을 하나 짚어서 물어보시죠.";
                case Speech.Haeyo: return "누굴 봤는지는… 시간을 딱 짚어서 물어봐 주세요.";
                default: return "누굴 봤는지는 시간을 콕 집어서 물어봐.";
            }
        }

        public static string NeverSaw(Speech s, string person)
        {
            switch (s)
            {
                case Speech.Hapsyo: return $"오늘 밤 {Josa.EulReul(person)} 마주친 기억은 없습니다.";
                case Speech.Haeyo: return $"오늘 밤엔 {Josa.EulReul(person)} 못 본 것 같아요.";
                default: return $"오늘 밤엔 {Josa.EulReul(person)} 못 봤어.";
            }
        }

        public static string Shrug(Speech s)
        {
            switch (s)
            {
                case Speech.Hapsyo: return "그 부분은 드릴 말씀이 없습니다.";
                case Speech.Haeyo: return "그건… 잘 모르겠어요.";
                default: return "그건 몰라.";
            }
        }

        public static string Deny(Speech s)
        {
            switch (s)
            {
                case Speech.Hapsyo: return "저를 의심하시는 겁니까? 저는 아무도 해치지 않았습니다.";
                case Speech.Haeyo: return "저, 저 아니에요! 정말이에요!";
                default: return "내가 왜? 헛소리하지 마.";
            }
        }

        public static string Evade(Speech s)
        {
            switch (s)
            {
                case Speech.Hapsyo: return "…기억이 확실하지 않군요. 그 이상은 말씀드릴 게 없습니다.";
                case Speech.Haeyo: return "그, 그건… 제가 착각했나 봐요. 더는 모르겠어요.";
                default: return "…기억 안 나. 착각했나 보지. 그만 물어.";
            }
        }

        public static string Maintain(Speech s)
        {
            switch (s)
            {
                case Speech.Hapsyo: return "제 진술은 정확합니다. 틀린 건 다른 쪽입니다.";
                case Speech.Haeyo: return "전 거짓말 안 했어요. 정말 그랬다니까요!";
                default: return "난 본 대로 말했어. 거짓말하는 건 내가 아냐.";
            }
        }

        public static string Confess(Speech s, int slot, string room, string secret)
        {
            string t = AlibiCase.SpokenTime(slot);
            string said = SecretSpoken(s, secret);
            switch (s)
            {
                case Speech.Hapsyo: return $"…인정하죠. {t}에는 사실 {room}에 있었습니다. {said}. 하지만 살인과는 무관합니다.";
                case Speech.Haeyo: return $"…말할게요. {t}엔 사실 {room}에 있었어요. {said}. 근데 살인이랑은 진짜 상관없어요!";
                default: return $"…알았어, 말할게. {t}엔 사실 {room}에 있었어. {said}. 살인이랑은 상관없어.";
            }
        }

        /// <summary>
        /// 비밀 서술문(과거형 '…다')을 대사체로. 비밀 문장은 전부 과거 어간(ㅆ)+'다'로 저작돼 있어서
        /// '다'를 떼고 어미만 붙이면 된다(뒤졌다→뒤졌어/뒤졌어요/뒤졌습니다).
        /// </summary>
        public static string SecretSpoken(Speech s, string secret)
        {
            string stem = secret.EndsWith("다") ? secret.Substring(0, secret.Length - 1) : secret;
            switch (s)
            {
                case Speech.Hapsyo: return stem + "습니다";
                case Speech.Haeyo: return stem + "어요";
                default: return stem + "어";
            }
        }
    }
}
