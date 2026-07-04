using System;

namespace MafiaAI.LLM
{
    /// <summary>LLM/진행 관련 튜닝값.</summary>
    [Serializable]
    public class GameConfig
    {
        public string BaseUrl = "http://localhost:11434";
        public string Model = "gemma4:12b"; // 심리전용 상위 모델(M5 Max 실측 58.7tok/s). 주의: 씬/프리팹 serialized 값이 실효값

        public int RoomCount = 5;           // 저택 방 개수(복도는 항상 방 개수-1개, 신장 트리 간선 수와 같음)
        public int DiscussionSeconds = 60;  // 낮 자유 토론 지속 시간
        public int NightSeconds = 15;       // 밤 지속 시간(화면에 보이는 카운트다운, 인간의 결정 시한)
        public float AiNightTimeoutSeconds = 40f; // AI의 밤 판단(LLM 호출) 시한 — NightSeconds와 별개로 넉넉하게
        public int VoteSeconds = 15;              // 투표 화면 타이머(인간의 결정 시한)
        public float AiVoteTimeoutSeconds = 40f;  // AI의 투표 판단(LLM 호출) 시한 — VoteSeconds와 별개로 넉넉하게
        public int TalkIntervalMinMs = 3000; // AI 문답 최소 간격
        public int TalkIntervalMaxMs = 5000; // AI 문답 최대 간격
        public int MoveIntervalMs = 7000;    // NPC 방 이동 간격
        public int LineDelayMs = 350;        // 짧은 UI 갱신 여유
        public int MaxDays = 15;            // 안전 상한

        public float SpeechTemperature = 1.05f; // 자유 발언 온도(12B 모델은 페르소나 자율성을 살림)
        public float ActionTemperature = 0.5f;  // 투표/밤 판단은 JSON 파싱을 위해 발언보다 낮게 유지

        public bool RevealRolesOnDeath = false; // 사망/처형 시 역할 공개. 2026-07-05 기획 변경: 비공개(추리 난이도·긴장 유지)

        // ── 밤 공간 사냥(인간 마피아 전용) ──
        public bool SpatialNightHunt = true;      // 인간이 마피아면 밤에 직접 접근해 Space로 살해
        public float KillRadius = 1.6f;           // 이 반경 안의 대상만 살해 가능(월드 단위)
        public float MafiaNightVision = 6.0f;     // 마피아일 때 밤 시야 반경(넓음)
        public float CitizenNightVision = 2.3f;   // 비마피아일 때 밤 시야 반경(좁음)
    }
}
