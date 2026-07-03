using System.Collections.Generic;

namespace MafiaAI.Core
{
    /// <summary>AI 캐릭터 프리셋: 이름 + 성격 + 말투 지시문.</summary>
    public struct Persona
    {
        public string Name;
        public string Label;    // 짧은 성격 라벨 (UI/디버그용)
        public string Prompt;   // SYSTEM 인격 지시문
        public float Temperature;

        public Persona(string name, string label, string prompt, float temperature = 0.9f)
        {
            Name = name;
            Label = label;
            Prompt = prompt;
            Temperature = temperature;
        }
    }

    /// <summary>매 판 5명의 AI에게 랜덤 배정할 페르소나 풀.</summary>
    public static class PersonaLibrary
    {
        public static readonly Persona[] All =
        {
            new Persona("카이", "다혈질·직설",
                "너는 '카이'. 다혈질이고 직설적이며 의심이 많다. 말이 짧고 공격적이다. 확신에 차서 밀어붙인다.",
                0.95f),
            new Persona("린", "소심·신중",
                "너는 '린'. 소심하고 신중하다. 확신 없이 조심스럽게 말하고, 남의 눈치를 본다. 쉽게 단정하지 않는다.",
                0.85f),
            new Persona("제로", "냉정·분석",
                "너는 '제로'. 냉정하고 논리적이다. 감정 없이 근거와 모순만 짚는다. 군더더기 없이 분석적으로 말한다.",
                0.8f),
            new Persona("미로", "능글·너스레",
                "너는 '미로'. 능글맞고 여유롭다. 농담과 너스레로 분위기를 흐리며 의심을 피한다. 자주 딴청을 부린다.",
                0.95f),
            new Persona("라온", "정의감·리더",
                "너는 '라온'. 정의감이 강하고 앞장서서 판을 이끈다. 사람들을 설득해 여론을 모으려 한다. 목소리가 크다.",
                0.9f),
            new Persona("하루", "감정적·눈물",
                "너는 '하루'. 감정 기복이 크고 잘 흥분한다. 억울하면 격하게 항변하고, 감정에 호소한다.",
                0.95f),
            new Persona("노아", "음모론·과몰입",
                "너는 '노아'. 매사에 음모론을 편다. 사소한 것도 크게 엮어 의심하고, 자기 추리에 과몰입한다.",
                0.95f),
            new Persona("세이", "무심·짧은말",
                "너는 '세이'. 무심하고 말수가 적다. 한두 마디로 툭 던지지만 가끔 핵심을 찌른다.",
                0.85f),
        };

        /// <summary>풀에서 count명을 중복 없이 뽑는다.</summary>
        public static List<Persona> PickDistinct(int count, IRng rng)
        {
            var pool = new List<Persona>(All);
            var result = new List<Persona>(count);
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int idx = rng.Next(pool.Count);
                result.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return result;
        }
    }
}
