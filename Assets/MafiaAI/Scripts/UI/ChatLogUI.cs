using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 하이라키에 직접 배치한 채팅 로그(ScrollRect + Content)에 GameController.OnLog를 그대로 찍는다.
    /// UI는 코드로 만들지 않는다 — chatLinePrefab(텍스트 한 줄짜리 프리팹)만 미리 만들어두면 된다.
    /// </summary>
    public class ChatLogUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] ScrollRect scrollRect;
        [SerializeField] Transform chatContent;
        [SerializeField] TMP_Text chatLinePrefab;

        [Header("선택: 로그 패널 토글")]
        [SerializeField] GameObject chatPanelRoot;
        [SerializeField] Button toggleButton;
        [SerializeField] TMP_Text toggleButtonLabel;

        static readonly Color TextColor = new Color32(0xE8, 0xE8, 0xEC, 0xFF);
        static readonly Color DeathColor = new Color32(0xE0, 0x50, 0x3A, 0xFF);
        static readonly Color RevealColor = new Color32(0xE0, 0xA0, 0x30, 0xFF);
        static readonly Color DimColor = new Color32(0x8A, 0x8A, 0x99, 0xFF);

        void Reset() => controller = GetComponent<GameController>();

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (toggleButton != null) toggleButton.onClick.AddListener(ToggleChatPanel);
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnLog += HandleLog;
            controller.OnGameEnd += HandleEnd;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnLog -= HandleLog;
            controller.OnGameEnd -= HandleEnd;
        }

        void Update()
        {
            if (chatPanelRoot == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) ToggleChatPanel();
        }

        void ToggleChatPanel()
        {
            if (chatPanelRoot == null) return;
            bool visible = !chatPanelRoot.activeSelf;
            chatPanelRoot.SetActive(visible);
            if (toggleButtonLabel != null) toggleButtonLabel.text = visible ? "로그 숨김" : "로그 보기";
        }

        void HandleLog(LogEntry e)
        {
            if (e.Kind == LogKind.Speech && !CanHear(e.Text)) return;
            Color col;
            string text;
            switch (e.Kind)
            {
                case LogKind.Speech: text = e.Text; col = TextColor; break;
                case LogKind.Death: text = "[사망] " + e.Text; col = DeathColor; break;
                case LogKind.Reveal: text = "◆ " + e.Text; col = RevealColor; break;
                case LogKind.Vote: text = "· " + e.Text; col = DimColor; break;
                default: text = "- " + e.Text; col = DimColor; break;
            }
            AddLine(text, col);
        }

        bool CanHear(string text)
        {
            if (controller.HumanPlayer == null) return true;
            // 투표 페이즈는 방 구분 없이 전원이 함께 대화한다(방 필터 해제).
            if (controller.State != null && controller.State.Phase == Phase.Vote) return true;
            string room = controller.GetPlayerRoom(controller.HumanPlayer.Id);
            if (string.IsNullOrEmpty(room)) return true;
            return text.StartsWith("[" + room + "]");
        }

        void HandleEnd(Winner w)
        {
            string side = w == Winner.Mafia ? "마피아" : (w == Winner.Citizens ? "시민" : "무승부");
            AddLine("◆ 게임 종료 — <b>" + side + " 진영 승리!</b>", w == Winner.Mafia ? DeathColor : RevealColor);
        }

        void AddLine(string text, Color color)
        {
            if (chatLinePrefab == null || chatContent == null) return;
            var line = Instantiate(chatLinePrefab, chatContent);
            line.text = text;
            line.color = color;
            line.textWrappingMode = TextWrappingModes.Normal;
            line.overflowMode = TextOverflowModes.Overflow;
            var rt = line.rectTransform;
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
            rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y);
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
