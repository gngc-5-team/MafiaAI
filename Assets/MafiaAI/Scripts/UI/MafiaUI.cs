using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 다크 누아르 취조실 UI를 코드로 생성하고 GameController에 바인딩한다.
    /// - 상단: Day/페이즈 헤더 + 6인 아바타 상태 바
    /// - 중앙: 대화 스트림(스크롤)
    /// - 하단: 당신의 정체 + 발언 입력 / 선택 오버레이
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public class MafiaUI : MonoBehaviour
    {
        [Tooltip("끄면 6명 전원 AI로 자동 진행(관전 모드). 켜면 한 좌석이 당신.")]
        public bool enableHumanSeat = true;

        // 팔레트
        static readonly Color BG       = Hex("0E0E14");
        static readonly Color PANEL    = Hex("16161F");
        static readonly Color PANEL2   = Hex("1E1E2A");
        static readonly Color ACCENT   = Hex("C0392B"); // 누아르 레드
        static readonly Color TEXT     = Hex("E8E8EC");
        static readonly Color DIM      = Hex("8A8A99");
        static readonly Color NAMECOL  = Hex("E0A030"); // 앰버
        static readonly Color DEATHCOL = Hex("E0503A");
        static readonly Color DEADBG   = Hex("101014");

        GameController _controller;
        HumanActor _human;
        Font _font;
        Sprite _white;

        [Header("씬(uicanvas)에 배치된 정적 UI 참조 — 인스펙터에서 편집 가능")]
        [SerializeField] Font _uiFont;
        [SerializeField] Text _headerText;
        [SerializeField] Text _timerText;
        [SerializeField] Text _roleText;
        [SerializeField] RectTransform _chatContent;
        [SerializeField] ScrollRect _scroll;
        [SerializeField] GameObject _chatPanel;
        [SerializeField] Button _chatToggleBtn;
        [SerializeField] Text _chatToggleLabel;
        [SerializeField] RectTransform _avatarBar;
        [SerializeField] InputField _input;
        [SerializeField] GameObject _inputBar;
        [SerializeField] Button _sendBtn;
        [SerializeField] Button _pressBtn;
        [SerializeField] Text _pressLabel;
        [SerializeField] GameObject _overlay;
        [SerializeField] RectTransform _overlayButtons;
        [SerializeField] Text _overlayTitle;

        bool _chatVisible = true;
        readonly Dictionary<string, AvatarCard> _cards = new();
        readonly List<string> _pressCandidates = new();
        int _pressIndex;

        class AvatarCard
        {
            public GameObject Root;
            public Image Bg;
            public Text Name;
            public Text Status;
        }

        void Start()
        {
            EnsureEventSystem();
            _font = _uiFont != null
                ? _uiFont
                : Font.CreateDynamicFontFromOSFont(
                    new[] { "Apple SD Gothic Neo", "AppleGothic", "Arial Unicode MS", "Malgun Gothic", "Arial" }, 16);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var tex = Texture2D.whiteTexture;
            _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

            _controller = GetComponent<GameController>();
            if (GetComponent<SpriteMansionView>() == null) gameObject.AddComponent<SpriteMansionView>();
            _controller.autoStart = false;
            _controller.includeHuman = enableHumanSeat;
            _controller.revealRolesAtStartForDebug = false;

            if (enableHumanSeat)
            {
                _human = new HumanActor();
                _human.OnNeedSpeech += HandleNeedSpeech;
                _human.OnNeedChoice += HandleNeedChoice;
                _human.OnNeedSpatialKill += HandleNeedSpatialKill;
                _human.OnChoiceResolved += delegate { if (_overlay != null) _overlay.SetActive(false); };
                _controller.humanActor = _human;
            }

            _controller.OnGameSetup += HandleSetup;
            _controller.OnLog += HandleLog;
            _controller.OnPhaseChanged += HandlePhase;
            _controller.OnLocationsChanged += RefreshMap;
            _controller.OnPlayerQuestion += HandlePlayerQuestion;
            _controller.OnGameEnd += HandleEnd;

            BindUI();
            _ = _controller.StartGameAsync();
        }

        void Update()
        {
            UpdateTimer();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.tabKey.wasPressedThisFrame) ToggleChatPanel();

            if (_input != null && _input.interactable)
            {
                bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _input.gameObject;
                if (!selected && keyboard.enterKey.wasPressedThisFrame) _input.ActivateInputField();
                if (selected && keyboard.escapeKey.wasPressedThisFrame) _input.DeactivateInputField();
            }
        }

        void UpdateTimer()
        {
            if (_timerText == null) return;
            if (_controller == null || _controller.State == null) { _timerText.text = ""; return; }

            var phase = _controller.State.Phase;
            float endsAt = _controller.PhaseEndsAt;
            if (endsAt > 0f && (phase == Phase.Discuss || phase == Phase.Night))
            {
                int remain = Mathf.Max(0, Mathf.CeilToInt(endsAt - Time.realtimeSinceStartup));
                bool night = phase == Phase.Night;
                _timerText.text = (night ? "밤 " : "낮 ") + remain + "초";
                _timerText.color = remain <= 5
                    ? Hex("E0503A")
                    : (night ? Hex("6FA8DC") : Hex("E0A030"));
            }
            else
            {
                _timerText.text = "";
            }
        }

        // ================= UI 바인딩 (씬 uicanvas의 정적 UI를 코드에 연결) =================
        // 정적 UI는 씬에 미리 배치되어 있다. 여기서는 참조 확인 + 버튼/입력 이벤트만 연결한다.
        void BindUI()
        {
            if (_chatToggleBtn != null)
            {
                _chatToggleBtn.onClick.AddListener(ToggleChatPanel);
                if (_chatToggleLabel == null) _chatToggleLabel = _chatToggleBtn.GetComponentInChildren<Text>();
            }
            if (_input != null)
                _input.onSubmit.AddListener(delegate { SubmitSpeech(); });
            if (_sendBtn != null)
                _sendBtn.onClick.AddListener(SubmitSpeech);
            if (_pressBtn != null)
            {
                _pressBtn.onClick.AddListener(CyclePress);
                if (_pressLabel == null) _pressLabel = _pressBtn.GetComponentInChildren<Text>();
            }

            if (_overlay != null) _overlay.SetActive(false);
            SetInputActive(false, "여기에 발언을 입력하세요… (당신 차례에 활성화)");
        }

        void ToggleChatPanel()
        {
            _chatVisible = !_chatVisible;
            if (_chatPanel != null) _chatPanel.SetActive(_chatVisible);
            if (_chatToggleLabel != null) _chatToggleLabel.text = _chatVisible ? "로그 숨김" : "로그 보기";
        }

        // ================= 이벤트 핸들러 =================
        void HandleSetup()
        {
            foreach (Transform c in _avatarBar) Destroy(c.gameObject);
            _cards.Clear();
            foreach (var p in _controller.State.Players)
            {
                var card = new GameObject("Card_" + p.Id, typeof(RectTransform), typeof(Image));
                var crt = (RectTransform)card.transform;
                crt.SetParent(_avatarBar, false);
                var bg = card.GetComponent<Image>();
                bg.sprite = _white; bg.type = Image.Type.Sliced;
                bg.color = p.IsHuman ? Color.Lerp(PANEL2, ACCENT, 0.4f) : PANEL2;

                var name = MakeText(crt, "Name", p.Id + (p.IsHuman ? " (나)" : ""), 18, p.IsHuman ? NAMECOL : TEXT, TextAnchor.MiddleCenter);
                SetAnchors((RectTransform)name.transform, new Vector2(0, 0.5f), new Vector2(1, 1));
                SetOffsets((RectTransform)name.transform, 4, 0, -4, -6);
                var status = MakeText(crt, "Status", "생존", 13, DIM, TextAnchor.MiddleCenter);
                SetAnchors((RectTransform)status.transform, new Vector2(0, 0), new Vector2(1, 0.5f));
                SetOffsets((RectTransform)status.transform, 4, 6, -4, 0);

                _cards[p.Id] = new AvatarCard { Root = card, Bg = bg, Name = name, Status = status };
            }
            RefreshRolePanel();
            RefreshMap();
        }

        void RefreshMap()
        {
            if (_controller == null || _controller.State == null) return;
            // 중앙 맵은 월드공간(SpriteMansionView)이 그린다. 여기선 추궁 후보/역할 패널만 갱신.
            BuildPressCandidates();
            RefreshRolePanel();
        }

        void HandleLog(LogEntry e)
        {
            if (e.Kind == LogKind.Speech && !CanHear(e.Text)) return;
            Color col;
            string text;
            switch (e.Kind)
            {
                case LogKind.Speech: text = "<b>" + e.Speaker + "</b>   " + e.Text; col = TEXT; break;
                case LogKind.Death:  text = "[사망] " + e.Text; col = DEATHCOL; break;
                case LogKind.Reveal: text = "◆ " + e.Text; col = NAMECOL; break;
                case LogKind.Vote:   text = "· " + e.Text; col = DIM; break;
                default:             text = "▸ " + e.Text; col = DIM; break;
            }
            AddChatLine(text, col);
            RefreshAvatars();
        }

        bool CanHear(string text)
        {
            if (_controller == null || _controller.HumanPlayer == null) return true;
            string room = _controller.GetPlayerRoom(_controller.HumanPlayer.Id);
            if (string.IsNullOrEmpty(room)) return true;
            return text.StartsWith("[" + room + "]");
        }

        void HandlePlayerQuestion(string asker, string room, string question)
        {
            if (_controller.HumanPlayer == null) return;
            if (_controller.GetPlayerRoom(_controller.HumanPlayer.Id) != room) return;
            SetInputActive(true, asker + "의 질문에 답하세요: " + question);
            _input.ActivateInputField();
        }

        void HandlePhase(GameState s)
        {
            string ph;
            switch (s.Phase)
            {
                case Phase.Night: ph = "밤"; break;
                case Phase.Dawn: ph = "새벽"; break;
                case Phase.Discuss: ph = "낮 토론"; break;
                case Phase.Vote: ph = "투표"; break;
                case Phase.End: ph = "종료"; break;
                default: ph = s.Phase.ToString(); break;
            }
            if (_headerText != null) _headerText.text = "DAY " + s.Day + " · " + ph;

            // 페이즈가 바뀌면 선택 오버레이는 닫는다(밤 시간초과 자동선택 후 정리 포함)
            if (_overlay != null) _overlay.SetActive(false);

            // 낮 토론 동안에는 입력창을 계속 열어 자유 채팅으로 쓴다
            bool canTalk = s.Phase == Phase.Discuss
                           && _controller.HumanPlayer != null && _controller.HumanPlayer.Alive;
            if (canTalk)
            {
                BuildPressCandidates();
                SetInputActive(true, "Enter로 채팅 입력. WASD로 이동. [지목: 이름]으로 특정인에게 답을 유도");
            }
            else if (s.Phase != Phase.Discuss)
            {
                SetInputActive(false, "지금은 발언할 수 없습니다 (낮 토론에 활성화)");
            }

            RefreshAvatars();
            RefreshRolePanel();
            RefreshMap();
        }

        void BuildPressCandidates()
        {
            _pressCandidates.Clear();
            var self = _controller.HumanPlayer;
            if (self == null) return;
            string myRoom = _controller.GetPlayerRoom(self.Id);
            foreach (var p in _controller.State.Alive)
                if (p.Id != self.Id && _controller.GetPlayerRoom(p.Id) == myRoom) _pressCandidates.Add(p.Id);
            _pressIndex = 0;
            UpdatePressLabel();
        }

        void HandleEnd(Winner w)
        {
            string side = w == Winner.Mafia ? "마피아" : (w == Winner.Citizens ? "시민" : "무승부");
            AddChatLine("◆ 게임 종료 — <b>" + side + " 진영 승리!</b>", w == Winner.Mafia ? DEATHCOL : NAMECOL);
        }

        // 자유 토론 방식으로 바뀌어 더는 호출되지 않지만, 호환을 위해 남겨둔다.
        void HandleNeedSpeech(Player self)
        {
            BuildPressCandidates();
            SetInputActive(true, "토론 중 — 언제든 입력 후 Enter");
        }

        void CyclePress()
        {
            if (_pressCandidates.Count == 0) return;
            _pressIndex = (_pressIndex + 1) % (_pressCandidates.Count + 1);
            UpdatePressLabel();
        }

        void UpdatePressLabel()
        {
            if (_pressLabel == null) return;
            _pressLabel.text = _pressIndex == 0 ? "추궁: 없음" : "추궁: " + _pressCandidates[_pressIndex - 1];
            _pressBtn.GetComponent<Image>().color = _pressIndex == 0 ? PANEL : Color.Lerp(PANEL, ACCENT, 0.6f);
        }

        // 인간 마피아의 밤: 오버레이 대신 저택에서 직접 접근해 Space로 살해한다.
        void HandleNeedSpatialKill(Player self, List<string> candidates)
        {
            if (_overlay != null) _overlay.SetActive(false);
            SetInputActive(false, "[밤] 어둠 속에서 대상에게 접근한 뒤 Space로 살해하라");
        }

        void HandleNeedChoice(Player self, List<string> candidates, string kind)
        {
            string title;
            switch (kind)
            {
                case "vote": title = "누구를 처형에 투표하시겠습니까?"; break;
                case "mafia": title = "[밤] 제거할 대상을 고르세요"; break;
                case "police": title = "[밤] 조사할 대상을 고르세요"; break;
                case "doctor": title = "[밤] 보호할 대상을 고르세요"; break;
                default: title = "대상을 고르세요"; break;
            }
            _overlayTitle.text = title;
            foreach (Transform c in _overlayButtons) Destroy(c.gameObject);
            foreach (var id in candidates)
            {
                string target = id;
                MakeButton(_overlayButtons, "Opt_" + id, id, PANEL, delegate
                {
                    _human.SubmitChoice(target);
                    _overlay.SetActive(false);
                });
            }
            _overlay.SetActive(true);
        }

        // ================= 헬퍼 =================
        void SubmitSpeech()
        {
            if (_input == null || !_input.interactable) return;
            string t = _input.text;
            if (string.IsNullOrWhiteSpace(t)) return;
            _input.text = "";
            string target = (_pressIndex > 0 && _pressIndex <= _pressCandidates.Count)
                ? _pressCandidates[_pressIndex - 1] : null;
            _pressIndex = 0;
            UpdatePressLabel();
            _controller.SubmitHumanMessage(t, target);   // 자유 채팅: 큐에 넣고 계속 입력 가능
            _input.DeactivateInputField();
        }

        void SetInputActive(bool on, string placeholder)
        {
            _input.interactable = on;
            _sendBtn.interactable = on;
            if (_pressBtn != null) _pressBtn.interactable = on;
            ((Text)_input.placeholder).text = placeholder;
            _sendBtn.GetComponent<Image>().color = on ? ACCENT : PANEL;
        }

        void RefreshRolePanel()
        {
            if (_roleText == null || _controller.HumanPlayer == null) return;
            var h = _controller.HumanPlayer;
            string secret = "당신의 정체: <b>" + h.Id + "</b> · <color=#" +
                            ColorUtility.ToHtmlStringRGB(NAMECOL) + ">" + h.Role.Korean() + "</color>" +
                            "    현재 위치: <color=#9BE66D>" + _controller.GetPlayerRoom(h.Id) + "</color>";
            if (h.Role == Role.Police && h.Investigations.Count > 0)
            {
                var parts = new List<string>();
                foreach (var r in h.Investigations) parts.Add(r.TargetId + "=" + r.Result.Korean());
                secret += "    [조사] " + string.Join(", ", parts);
            }
            if (!h.Alive) secret += "    (사망)";
            _roleText.text = secret;
        }

        void RefreshAvatars()
        {
            if (_controller.State == null) return;
            foreach (var p in _controller.State.Players)
            {
                if (!_cards.TryGetValue(p.Id, out var card)) continue;
                if (p.Alive)
                {
                    card.Status.text = "생존";
                    card.Status.color = DIM;
                    card.Bg.color = p.IsHuman ? Color.Lerp(PANEL2, ACCENT, 0.4f) : PANEL2;
                    card.Name.color = p.IsHuman ? NAMECOL : TEXT;
                }
                else
                {
                    card.Status.text = "사망 · " + p.Role.Korean();
                    card.Status.color = DEATHCOL;
                    card.Bg.color = DEADBG;
                    card.Name.color = DIM;
                }
            }
        }

        void AddChatLine(string msg, Color col)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = 17; t.color = col; t.text = msg;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = TextAnchor.UpperLeft;
            go.transform.SetParent(_chatContent, false);
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        // ---- 저수준 UI 팩토리 ----
        RectTransform MakePanel(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = _white; img.type = Image.Type.Sliced; img.color = color;
            return rt;
        }

        Text MakeText(RectTransform parent, string name, string content, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            SetStretch(rt, 0, 0, 0, 0);
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = size; t.color = color; t.text = content;
            t.alignment = anchor; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        Button MakeButton(RectTransform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = _white; img.type = Image.Type.Sliced; img.color = color;
            go.GetComponent<Button>().onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 44; le.preferredHeight = 44;
            var t = MakeText(rt, "Label", label, 18, TEXT, TextAnchor.MiddleCenter);
            SetStretch(t, 0, 0, 0, 0);
            return go.GetComponent<Button>();
        }

        // ---- RectTransform 유틸 ----
        static void SetStretch(Component c, float l, float b, float r, float t)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(r, t);
        }
        static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
        { rt.anchorMin = min; rt.anchorMax = max; }
        static void SetOffsets(RectTransform rt, float l, float b, float r, float t)
        { rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(r, t); }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
