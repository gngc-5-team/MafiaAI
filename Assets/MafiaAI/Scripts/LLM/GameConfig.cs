using System;

namespace MafiaAI.LLM
{
    /// <summary>LLM/진행 관련 튜닝값.</summary>
    [Serializable]
    public class GameConfig
    {
        public string BaseUrl = "http://localhost:11434";
        public string Model = "gemma3:4b";

        public int RoomCount = 5;           // 저택 방 개수(복도는 항상 방 개수-1개, 신장 트리 간선 수와 같음)
        public int DiscussionSeconds = 60;  // 낮 자유 토론 지속 시간
        public int NightSeconds = 10;       // 밤 지속 시간(인간 능력자 선택 제한)
        public int TalkIntervalMinMs = 3000; // AI 문답 최소 간격
        public int TalkIntervalMaxMs = 5000; // AI 문답 최대 간격
        public int MoveIntervalMs = 7000;    // NPC 방 이동 간격
        public int LineDelayMs = 350;        // 짧은 UI 갱신 여유
        public int MaxDays = 15;            // 안전 상한

        public float SpeechTemperature = 0.95f; // 자유 발언 온도(페르소나가 덮어씀)
        public float ActionTemperature = 0.4f;  // 투표/밤 판단 온도(안정적으로)

        public bool RevealRolesOnDeath = true;  // 사망/처형 시 역할 공개(기획서 기준)
    }
}
