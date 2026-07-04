using UnityEngine;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 하이라키에 직접 배치한 TopBar(PhaseChip/PhaseText, TimerText, PlayerCount)를
    /// GameController 상태에 맞춰 갱신한다. MafiaUI.cs의 헤더/타이머 로직을 그대로 옮긴 것 —
    /// UI는 코드로 만들지 않고 참조만 한다.
    /// </summary>
    public class TopBarUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] TMP_Text phaseText;
        [SerializeField] TMP_Text timerText;
        [SerializeField] TMP_Text playerCountText;

        static readonly Color NightColor = new Color32(0x6F, 0xA8, 0xDC, 0xFF);
        static readonly Color DayColor = new Color32(0xE0, 0xA0, 0x30, 0xFF);
        static readonly Color UrgentColor = new Color32(0xE0, 0x50, 0x3A, 0xFF);

        void Reset()
        {
            controller = GetComponent<GameController>();
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (controller == null) controller = FindFirstObjectByType<GameController>();
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnPhaseChanged += HandlePhaseChanged;
            controller.OnGameSetup += RefreshPlayerCount;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnPhaseChanged -= HandlePhaseChanged;
            controller.OnGameSetup -= RefreshPlayerCount;
        }

        void Update()
        {
            UpdateTimer();
        }

        void HandlePhaseChanged(GameState s)
        {
            if (phaseText != null) phaseText.text = "DAY " + s.Day + " · " + PhaseLabel(s.Phase);
            RefreshPlayerCount();
        }

        void RefreshPlayerCount()
        {
            if (playerCountText == null || controller.State == null) return;
            string label = "생존자";
            playerCountText.text = label +controller.State.AliveList.Count + "/" + controller.State.Players.Count;
        }

        void UpdateTimer()
        {
            if (timerText == null) return;
            if (controller == null || controller.State == null) { timerText.text = ""; return; }

            var phase = controller.State.Phase;
            float endsAt = controller.PhaseEndsAt;
            if (endsAt > 0f && (phase == Phase.Discuss || phase == Phase.Night))
            {
                int remain = Mathf.Max(0, Mathf.CeilToInt(endsAt - Time.realtimeSinceStartup));
                timerText.text = string.Format("{0:00}:{1:00}", remain / 60, remain % 60);
                timerText.color = remain <= 5 ? UrgentColor : (phase == Phase.Night ? NightColor : DayColor);
            }
            else
            {
                timerText.text = "";
            }
        }

        static string PhaseLabel(Phase p)
        {
            switch (p)
            {
                case Phase.Night: return "밤";
                case Phase.Dawn: return "새벽";
                case Phase.Discuss: return "토론";
                case Phase.Vote: return "투표";
                case Phase.End: return "종료";
                default: return p.ToString();
            }
        }
    }
}
