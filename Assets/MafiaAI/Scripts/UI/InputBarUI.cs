using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 하이라키에 직접 배치한 발언 입력창 + 대상 칩(@이름) + 전송 버튼을 GameController에 연결한다.
    /// 같은 방에 있는 사람 수만큼 chipPrefab을 찍고, 클릭해서 선택한 사람에게 콕 집어 말을 건다.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public class InputBarUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] TMP_InputField input;
        [SerializeField] Button sendButton;
        [SerializeField] Transform chipListRoot;
        [SerializeField] Button chipPrefab;

        static readonly Color ChipIdle = new Color32(0x16, 0x16, 0x1F, 0xFF);
        static readonly Color ChipSelected = new Color32(0xC0, 0x39, 0x2B, 0xFF);

        readonly Dictionary<string, Button> _chips = new();
        string _selectedTarget;

        void Reset() => controller = GetComponent<GameController>();

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (sendButton != null) sendButton.onClick.AddListener(SubmitSpeech);
            if (input != null) input.onSubmit.AddListener(delegate { SubmitSpeech(); });
            SetInputActive(false, "지금은 발언할 수 없습니다");
        }

        void Update()
        {
            if (input == null || !input.interactable) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == input.gameObject;
            if (!selected && kb.enterKey.wasPressedThisFrame) input.ActivateInputField();
            if (selected && kb.escapeKey.wasPressedThisFrame) DeactivateInput();
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnPhaseChanged += HandlePhaseChanged;
            controller.OnLocationsChanged += RebuildChips;
            controller.OnPlayerQuestion += HandlePlayerQuestion;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnPhaseChanged -= HandlePhaseChanged;
            controller.OnLocationsChanged -= RebuildChips;
            controller.OnPlayerQuestion -= HandlePlayerQuestion;
        }

        void HandlePhaseChanged(GameState s)
        {
            bool canTalk = s.Phase == Phase.Discuss && controller.HumanPlayer != null && controller.HumanPlayer.Alive;
            if (canTalk)
            {
                RebuildChips();
                SetInputActive(true, "Enter로 채팅 입력. WASD로 이동. 사람 칩을 눌러 콕 집어 말 걸기");
            }
            else
            {
                SetInputActive(false, "지금은 발언할 수 없습니다 (낮 토론에 활성화)");
            }
        }

        void HandlePlayerQuestion(string asker, string room, string question)
        {
            if (controller.HumanPlayer == null) return;
            if (controller.GetPlayerRoom(controller.HumanPlayer.Id) != room) return;
            SetInputActive(true, asker + "의 질문에 답하세요: " + question);
            if (input != null) input.ActivateInputField();
        }

        void RebuildChips()
        {
            if (chipListRoot == null || chipPrefab == null) return;
            foreach (Transform c in chipListRoot) Destroy(c.gameObject);
            _chips.Clear();
            _selectedTarget = null;

            var self = controller.HumanPlayer;
            if (self == null) return;

            string myRoom = controller.GetPlayerRoom(self.Id);
            foreach (var p in controller.State.Alive)
            {
                if (p.Id == self.Id || controller.GetPlayerRoom(p.Id) != myRoom) continue;
                string id = p.Id;
                var chip = Instantiate(chipPrefab, chipListRoot);
                var label = chip.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = "@" + id;
                chip.onClick.AddListener(() => ToggleChip(id));
                _chips[id] = chip;
            }
        }

        void ToggleChip(string id)
        {
            _selectedTarget = _selectedTarget == id ? null : id;
            foreach (var kv in _chips)
            {
                var img = kv.Value.GetComponent<Image>();
                if (img != null) img.color = kv.Key == _selectedTarget ? ChipSelected : ChipIdle;
            }
        }

        void ClearSelection()
        {
            _selectedTarget = null;
            foreach (var kv in _chips)
            {
                var img = kv.Value.GetComponent<Image>();
                if (img != null) img.color = ChipIdle;
            }
        }

        void SubmitSpeech()
        {
            if (input == null || !input.interactable) return;
            string t = input.text;
            if (string.IsNullOrWhiteSpace(t)) return;
            input.text = "";
            string target = _selectedTarget;
            ClearSelection();
            controller.SubmitHumanMessage(t, target);
            DeactivateInput();
        }

        /// <summary>
        /// DeactivateInputField()만 부르면 EventSystem이 여전히 이 입력창을 "선택된 오브젝트"로
        /// 기억해서 IsTyping() 판정이 안 풀린다(다른 데를 클릭해야만 풀림). 선택도 같이 해제한다.
        /// </summary>
        void DeactivateInput()
        {
            if (input == null) return;
            input.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        void SetInputActive(bool on, string placeholder)
        {
            if (input != null)
            {
                input.interactable = on;
                if (input.placeholder is TMP_Text ph) ph.text = placeholder;
            }
            if (sendButton != null) sendButton.interactable = on;
        }
    }
}
