using System.Collections.Generic;
using System.Threading;
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
    /// 플레이어 전용 1:1 심문(VN 스타일 오버레이) — planning/concept.png 레이아웃.
    /// UI는 씬의 InterrogationCanvas 하이라키에 미리 저작되어 있고(에디터에서 직접 수정),
    /// 이 스크립트는 참조만 받아 열고/닫고/대화 로직만 담당한다. 런타임 UI 생성 없음.
    /// 낮에 같은 방의 살아있는 AI 근처에서 F로 열림. 대화는 비공개(같은 방 사람도 못 들음).
    /// 낮이 끝나거나 ESC로 닫힌다. 오버레이는 게임 TopBar를 가리지 않는 높이로 저작되어 있다.
    /// </summary>
    public class InterrogationUI : MonoBehaviour
    {
        [Header("게임 연결")]
        [SerializeField] GameController controller;
        [SerializeField] SpriteMansionView mansionView;

        [System.Serializable]
        public class Portrait { public string playerId; public Sprite sprite; }

        [Header("에셋 (초상 = 이름별, 배경 = 랜덤 선택)")]
        [SerializeField] Portrait[] portraits;      // kai/haru/miro/noa/sei/zero → 카이/하루/미로/노아/세이/제로
        [SerializeField] Sprite[] backgrounds;      // background_art 3종

        [Header("씬 저작 UI 참조 (InterrogationCanvas 자식들)")]
        [SerializeField] GameObject overlayRoot;    // Overlay (기본 비활성)
        [SerializeField] Image bgImage;             // Overlay/Scene/Bg
        [SerializeField] Image portraitImage;       // Overlay/Scene/Portrait
        [SerializeField] TMP_Text convoText;        // Overlay/TextBox/Convo
        [SerializeField] TMP_InputField input;      // Overlay/TextBox/Input
        [SerializeField] GameObject hintRoot;       // HintPlate (기본 비활성)
        [SerializeField] TMP_Text hintText;         // HintPlate/Hint

        [Header("설정")]
        [SerializeField] float initiateRadius = 3.6f;   // 같은 방에서 이 반경 안 최근접 대상만 심문 가능
        [SerializeField] int convoMaxLines = 5;         // 텍스트박스에 보이는 최근 대화 줄 수

        // ── 대화 표기(전부 에디터에서 조절 — 코드는 위치/크기/정렬/폰트를 절대 건드리지 않는다) ──
        [Header("대화 표기 (위치·크기·정렬·폰트는 Convo 오브젝트에서 직접)")]
        [SerializeField] string myDisplayName = "나";
        [SerializeField] Color myNameColor = new Color(0.42f, 0.29f, 0.16f);      // #6B4A2A
        [SerializeField] Color targetNameColor = new Color(0.54f, 0.18f, 0.18f);  // #8A2E2E
        [SerializeField] Color introTextColor = new Color(0.54f, 0.48f, 0.38f);   // #8A7A62
        [SerializeField] bool boldSpeakerNames = true;
        [SerializeField] bool showIntroLine = true;
        [Tooltip("발화자 이름 뒤 구분(예: 공백 2칸, ': ' 등)")]
        [SerializeField] string speakerSeparator = "  ";

        HumanActor _human;
        bool _open;
        bool _busy;
        string _targetId;
        readonly List<string> _lines = new();
        CancellationTokenSource _cts;

        void Awake()
        {
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (mansionView == null) mansionView = FindFirstObjectByType<SpriteMansionView>();
            if (input != null) input.onSubmit.AddListener(SubmitQuestion);
            if (overlayRoot != null) overlayRoot.SetActive(false);
            if (hintRoot != null) hintRoot.SetActive(false);
        }

        void OnEnable()
        {
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (controller == null) return;
            controller.OnGameSetup += TryBindHuman;
            controller.OnPhaseChanged += HandlePhaseChanged;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnGameSetup -= TryBindHuman;
            controller.OnPhaseChanged -= HandlePhaseChanged;
        }

        void TryBindHuman() => _human = controller.humanActor as HumanActor;

        void HandlePhaseChanged(GameState s)
        {
            if (_open && (s == null || s.Phase != Phase.Discuss)) CloseInterrogation();
        }

        void Update()
        {
            if (controller == null || controller.State == null) return;

            if (_open)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) CloseInterrogation();
                return;
            }

            string near = CanStart() ? FindNearestSameRoomAI() : null;
            SetHint(near);
            if (near != null)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.fKey.wasPressedThisFrame) OpenInterrogation(near);
            }
        }

        bool CanStart()
        {
            if (_human == null || mansionView == null || overlayRoot == null) return false;
            var hp = controller.HumanPlayer;
            if (hp == null || !hp.Alive) return false;
            if (controller.State.Phase != Phase.Discuss) return false;
            if (IsTyping()) return false;
            return true;
        }

        string FindNearestSameRoomAI()
        {
            var hp = controller.HumanPlayer;
            string myRoom = controller.GetPlayerRoom(hp.Id);
            if (string.IsNullOrEmpty(myRoom)) return null;
            Vector3 me = mansionView.HumanWorldPosition;
            string best = null;
            float bestD = initiateRadius;
            foreach (var p in controller.State.Alive)
            {
                if (p.IsHuman || controller.GetPlayerRoom(p.Id) != myRoom) continue;
                if (!mansionView.TryGetTokenPosition(p.Id, out var pos)) continue;
                float d = Vector3.Distance(me, pos);
                if (d < bestD) { bestD = d; best = p.Id; }
            }
            return best;
        }

        // ---------- 열기/닫기 ----------

        void OpenInterrogation(string targetId)
        {
            _targetId = targetId;
            _open = true;
            _busy = false;
            _cts = new CancellationTokenSource();
            controller.BeginInterrogation(targetId);
            _lines.Clear();
            if (showIntroLine)
                _lines.Add("<i><color=#" + ColorUtility.ToHtmlStringRGB(introTextColor) + ">" + targetId + "을(를) 따로 불러냈다. 단둘이다.</color></i>");

            if (portraitImage != null)
            {
                portraitImage.sprite = PortraitFor(targetId);
                portraitImage.enabled = portraitImage.sprite != null;
            }
            if (bgImage != null && backgrounds != null && backgrounds.Length > 0)
            {
                bgImage.sprite = backgrounds[Random.Range(0, backgrounds.Length)];
                bgImage.enabled = bgImage.sprite != null;
            }
            RefreshConvo();
            SetHint(null);

            overlayRoot.SetActive(true);
            if (input != null)
            {
                input.text = "";
                input.interactable = true;
                input.ActivateInputField();
            }
        }

        void CloseInterrogation()
        {
            _open = false;
            _busy = false;
            if (controller != null) controller.EndInterrogation(); // 대상 이동 잠금 해제
            if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
            if (input != null) { input.DeactivateInputField(); input.text = ""; }
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        // ---------- 대화 ----------

        async void SubmitQuestion(string _)
        {
            if (!_open || _busy || input == null) return;
            string q = input.text != null ? input.text.Trim() : "";
            if (string.IsNullOrEmpty(q)) { input.ActivateInputField(); return; }

            _busy = true;
            input.text = "";
            input.interactable = false;
            AddLine(myDisplayName, q);
            AddLine(_targetId, "…");
            RefreshConvo();

            string reply = null;
            var ct = _cts != null ? _cts.Token : CancellationToken.None;
            try { reply = await controller.AskInterrogationAsync(_targetId, q, ct); }
            catch { reply = null; }

            if (!_open) return;
            if (_lines.Count > 0) _lines.RemoveAt(_lines.Count - 1); // "…" 자리 교체
            AddLine(_targetId, string.IsNullOrEmpty(reply) ? "(답을 흐린다)" : reply);
            RefreshConvo();

            _busy = false;
            input.interactable = true;
            input.ActivateInputField();
        }

        void AddLine(string who, string text)
        {
            bool me = who == myDisplayName;
            string col = ColorUtility.ToHtmlStringRGB(me ? myNameColor : targetNameColor);
            string name = boldSpeakerNames ? "<b>" + who + "</b>" : who;
            _lines.Add("<color=#" + col + ">" + name + "</color>" + speakerSeparator + text);
        }

        void RefreshConvo()
        {
            if (convoText == null) return;
            int start = Mathf.Max(0, _lines.Count - convoMaxLines);
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < _lines.Count; i++) sb.AppendLine(_lines[i]);
            convoText.text = sb.ToString().TrimEnd();
        }

        Sprite PortraitFor(string playerId)
        {
            if (portraits == null) return null;
            foreach (var p in portraits)
                if (p != null && p.playerId == playerId) return p.sprite;
            return null;
        }

        void SetHint(string near)
        {
            if (hintRoot == null || hintText == null) return;
            bool show = !_open && near != null;
            if (hintRoot.activeSelf != show) hintRoot.SetActive(show);
            if (show) hintText.text = "F  —  " + near + " 심문하기";
        }

        bool IsTyping()
        {
            var sel = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            return sel != null && sel.GetComponent<TMP_InputField>() != null;
        }
    }
}
