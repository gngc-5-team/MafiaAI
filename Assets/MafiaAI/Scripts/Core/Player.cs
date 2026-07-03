using System.Collections.Generic;

namespace MafiaAI.Core
{
    /// <summary>경찰이 특정 밤에 얻은 조사 결과.</summary>
    public struct InvestigationResult
    {
        public int Day;
        public string TargetId;
        public Faction Result;
    }

    /// <summary>플레이어 1명(인간 또는 AI).</summary>
    public class Player
    {
        public string Id;            // 표시 이름 겸 식별자, 예: "카이"
        public bool IsHuman;
        public Role Role;

        // 페르소나(AI 연기용)
        public string PersonaName;   // 성격 라벨, 예: "다혈질·직설"
        public string PersonaPrompt; // SYSTEM에 넣을 인격 지시문
        public float Temperature = 0.9f;

        public bool Alive = true;

        // 역할별 비밀 기록
        public readonly List<InvestigationResult> Investigations = new(); // 경찰
        public readonly List<string> ProtectLog = new();                  // 의사가 보호한 대상 id

        public Faction Faction => Role.GetFaction();

        public override string ToString() => Id;
    }
}
