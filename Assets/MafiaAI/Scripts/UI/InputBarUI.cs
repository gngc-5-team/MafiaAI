using System.Collections;
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
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (sendButton != null) sendButton.onClick.AddListener(SubmitSpeechDeferred);
            // Enter(onSubmit) 시점엔 한글 마지막 글자가 아직 IME 조합 중이라 input.text가 깨진다.
            // 한 프레임 미뤄 조합이 확정된 뒤 읽어 보낸다(한글 마지막 글자 누락/단일 글자 전송 버그 방지).
            if (input != null) input.onSubmit.AddListener(delegate { SubmitSpeechDeferred(); });
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

            // 이 입력창이 (투표창 안처럼) 페이즈가 바뀐 '뒤에' 켜졌다면 그 이벤트를 놓쳤을 수 있으니,
            // 켜진 즉시 현재 페이즈를 직접 확인해서 입력 활성/비활성을 맞춘다.
            if (controller.State != null) HandlePhaseChanged(controller.State);
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
            bool talkPhase = s.Phase == Phase.Discuss || s.Phase == Phase.Vote; // 투표 중에도 대화 허용
            bool canTalk = talkPhase && controller.HumanPlayer != null && controller.HumanPlayer.Alive;
            if (canTalk)
            {
                RebuildChips();
                SetInputActive(true, s.Phase == Phase.Vote
                    ? "투표 중 — Enter로 발언. 사람 칩을 눌러 콕 집어 말 걸기"
                    : "Enter로 채팅 입력. WASD로 이동. 사람 칩을 눌러 콕 집어 말 걸기");
            }
            else
            {
                SetInputActive(false, "지금은 발언할 수 없습니다 (토론·투표에 활성화)");
            }
        }

        void HandlePlayerQuestion(string asker, string room, string question)
        {
            if (controller.HumanPlayer == null) return;
            if (controller.GetPlayerRoom(controller.HumanPlayer.Id) != room) return;
            // 포커스를 강제로 뺏지 않는다 — 이동(WASD) 중 키가 입력창으로 새는 UX 문제.
            // 플레이서홀더로 질문만 알려주고, 답하고 싶을 때 Enter로 입력창을 연다(Update의 기존 동작).
            SetInputActive(true, asker + "의 질문 (Enter로 답하기): " + question);
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

        bool _submitting;

        /// <summary>Enter 전송: IME 조합 확정을 기다렸다가(한 프레임) 텍스트를 읽는다.</summary>
        void SubmitSpeechDeferred()
        {
            if (_submitting) return;   // Enter 연타로 인한 이중 전송 방지
            _submitting = true;
            StartCoroutine(SubmitAfterImeCommit());
        }

        IEnumerator SubmitAfterImeCommit()
        {
            // 조합 중이던 마지막 글자를 강제로 확정시킨다 — 비활성화하면 IME 조합이 input.text에 반영된다.
            if (input != null) input.DeactivateInputField();
            yield return null;                       // 다음 프레임까지 양보
            yield return new WaitForEndOfFrame();     // 그 프레임의 입력/IME 처리가 끝난 뒤 읽도록
            _submitting = false;
            SubmitSpeech();
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
