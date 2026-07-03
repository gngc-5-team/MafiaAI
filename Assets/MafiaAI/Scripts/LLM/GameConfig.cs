using System;

namespace MafiaAI.LLM
{
    /// <summary>LLM/진행 관련 튜닝값.</summary>
    [Serializable]
    public class GameConfig
    {
        public string BaseUrl = "http://localhost:11434";
        public string Model = "gemma3:4b";

        public int DiscussionSeconds = 60;  // 낮 자유 토론 지속 시간
        public int NightSeconds = 15;       // 밤 지속 시간(공간 사냥 이동 여유)
        public int TalkIntervalMinMs = 3000; // AI 문답 최소 간격
        public int TalkIntervalMaxMs = 5000; // AI 문답 최대 간격
        public int MoveIntervalMs = 7000;    // NPC 방 이동 간격
        public int LineDelayMs = 350;        // 짧은 UI 갱신 여유
        public int MaxDays = 15;            // 안전 상한

        public float SpeechTemperature = 0.95f; // 자유 발언 온도(페르소나가 덮어씀)
        public float ActionTemperature = 0.4f;  // 투표/밤 판단 온도(안정적으로)

        public bool RevealRolesOnDeath = true;  // 사망/처형 시 역할 공개(기획서 기준)

        // ── 밤 공간 사냥(인간 마피아 전용) ──
        public bool SpatialNightHunt = true;      // 인간이 마피아면 밤에 직접 접근해 Space로 살해
        public float KillRadius = 1.6f;           // 이 반경 안의 대상만 살해 가능(월드 단위)
        public float MafiaNightVision = 6.0f;     // 마피아일 때 밤 시야 반경(넓음)
        public float CitizenNightVision = 2.3f;   // 비마피아일 때 밤 시야 반경(좁음)
    }
}
