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

        Text _headerText;
        Text _timerText;
        Text _roleText;
        RectTransform _chatContent;
        ScrollRect _scroll;
        GameObject _chatPanel;
        Button _chatToggleBtn;
        Text _chatToggleLabel;
        bool _chatVisible = true;
        RectTransform _avatarBar;
        RectTransform _mapPanel;
        readonly Dictionary<string, RectTransform> _roomRects = new();
        readonly Dictionary<string, Text> _roomLabels = new();
        readonly Dictionary<string, RectTransform> _tokens = new();
        readonly Dictionary<string, AvatarCard> _cards = new();

        InputField _input;
        GameObject _inputBar;
        Button _sendBtn;
        Button _pressBtn;
        Text _pressLabel;
        readonly List<string> _pressCandidates = new();
        int _pressIndex;

        GameObject _overlay;
        RectTransform _overlayButtons;
        Text _overlayTitle;

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
            _font = Font.CreateDynamicFontFromOSFont(
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

            BuildUI();
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

        // ================= UI 빌드 =================
        void BuildUI()
        {
            var canvasGo = new GameObject("MafiaCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)canvasGo.transform;

            var bg = MakePanel(root, "BG", new Color(0, 0, 0, 0));
            SetStretch(bg, 0, 0, 0, 0);
            bg.GetComponent<Image>().raycastTarget = false;

            // 헤더
            var header = MakePanel(root, "Header", PANEL2);
            SetAnchors(header, new Vector2(0, 1), new Vector2(1, 1));
            SetOffsets(header, 0, -64, 0, 0);
            var title = MakeText(header, "Title", "AI 마피아", 24, NAMECOL, TextAnchor.MiddleLeft);
            SetOffsets((RectTransform)title.transform, 20, 0, -400, 0);
            _headerText = MakeText(header, "DayPhase", "준비 중…", 20, TEXT, TextAnchor.MiddleRight);
            SetOffsets((RectTransform)_headerText.transform, 400, 0, -20, 0);
            // 중앙 상단: 낮/밤 카운트다운 타이머
            _timerText = MakeText(header, "Timer", "", 30, ACCENT, TextAnchor.MiddleCenter);
            SetOffsets((RectTransform)_timerText.transform, 0, 0, 0, 0);
            ((Text)_timerText).fontStyle = FontStyle.Bold;

            // 좌측 대화 스트림: 내 방에서 들리는 말만 표시
            var chatPanel = MakePanel(root, "ChatPanel", PANEL);
            _chatPanel = chatPanel.gameObject;
            SetAnchors(chatPanel, new Vector2(0, 0), new Vector2(0, 1));
            SetOffsets(chatPanel, 8, 150, 320, -72);
            BuildScroll(chatPanel);

            _chatToggleBtn = MakeButton(root, "ChatToggle", "로그 숨김", PANEL2, ToggleChatPanel);
            var toggleRt = (RectTransform)_chatToggleBtn.transform;
            SetAnchors(toggleRt, new Vector2(0, 1), new Vector2(0, 1));
            SetOffsets(toggleRt, 8, -108, 118, -72);
            _chatToggleLabel = _chatToggleBtn.GetComponentInChildren<Text>();

            // 중앙은 World-space SpriteRenderer 저택 맵이 보이는 영역이다.
            _mapPanel = null;

            // 우측 생존자 바
            _avatarBar = MakePanel(root, "AvatarBar", PANEL);
            SetAnchors(_avatarBar, new Vector2(1, 0), new Vector2(1, 1));
            SetOffsets(_avatarBar, -320, 150, -8, -72);
            var vlgAvatar = _avatarBar.gameObject.AddComponent<VerticalLayoutGroup>();
            vlgAvatar.spacing = 8; vlgAvatar.padding = new RectOffset(8, 8, 8, 8);
            vlgAvatar.childControlWidth = true; vlgAvatar.childControlHeight = true;
            vlgAvatar.childForceExpandWidth = true; vlgAvatar.childForceExpandHeight = false;

            // 당신의 정체
            var roleBar = MakePanel(root, "RoleBar", PANEL2);
            SetAnchors(roleBar, new Vector2(0, 0), new Vector2(1, 0));
            SetOffsets(roleBar, 328, 76, -328, 140);
            _roleText = MakeText(roleBar, "RoleText", "당신의 정체: —", 17, TEXT, TextAnchor.MiddleLeft);
            SetOffsets((RectTransform)_roleText.transform, 14, 0, -14, 0);

            BuildInputBar(root);
            BuildOverlay(root);
        }

        void BuildMansionMap()
        {
            _roomRects.Clear();
            _roomLabels.Clear();
            AddRoom("서재", 0.08f, 0.58f, 0.28f, 0.92f, Hex("2A2118"));
            AddRoom("욕실", 0.36f, 0.66f, 0.50f, 0.92f, Hex("20272D"));
            AddRoom("식당", 0.56f, 0.58f, 0.93f, 0.92f, Hex("2A2018"));
            AddRoom("복도", 0.12f, 0.38f, 0.88f, 0.58f, Hex("181A1F"));
            AddRoom("응접실", 0.30f, 0.18f, 0.70f, 0.42f, Hex("222018"));
            AddRoom("현관", 0.42f, 0.02f, 0.58f, 0.18f, Hex("1B1B20"));
            AddRoom("휴게실", 0.72f, 0.14f, 0.94f, 0.38f, Hex("241A1A"));
        }

        void AddRoom(string room, float x0, float y0, float x1, float y1, Color color)
        {
            var rt = MakePanel(_mapPanel, "Room_" + room, color);
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = new Vector2(6, 6);
            rt.offsetMax = new Vector2(-6, -6);
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.75f, 0.25f, 0.18f);
            outline.effectDistance = new Vector2(1, -1);
            var btn = rt.gameObject.AddComponent<Button>();
            string r = room;
            btn.onClick.AddListener(delegate { _controller.MoveHumanToRoom(r); });
            var label = MakeText(rt, "Label", room, 19, NAMECOL, TextAnchor.UpperCenter);
            SetAnchors((RectTransform)label.transform, new Vector2(0, 1), new Vector2(1, 1));
            SetOffsets((RectTransform)label.transform, 4, -34, -4, -4);
            _roomRects[room] = rt;
            _roomLabels[room] = label;
        }

        void ToggleChatPanel()
        {
            _chatVisible = !_chatVisible;
            if (_chatPanel != null) _chatPanel.SetActive(_chatVisible);
            if (_chatToggleLabel != null) _chatToggleLabel.text = _chatVisible ? "로그 숨김" : "로그 보기";
        }

        void BuildScroll(RectTransform parent)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            var srt = (RectTransform)scrollGo.transform;
            srt.SetParent(parent, false);
            SetStretch(srt, 4, 4, -4, -4);
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.15f);
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.scrollSensitivity = 30;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var vrt = (RectTransform)viewport.transform;
            vrt.SetParent(srt, false);
            SetStretch(vrt, 0, 0, 0, 0);
            viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            _scroll.viewport = vrt;

            var content = new GameObject("Content", typeof(RectTransform));
            _chatContent = (RectTransform)content.transform;
            _chatContent.SetParent(vrt, false);
            _chatContent.anchorMin = new Vector2(0, 1);
            _chatContent.anchorMax = new Vector2(1, 1);
            _chatContent.pivot = new Vector2(0.5f, 1);
            _chatContent.anchoredPosition = Vector2.zero;
            _chatContent.sizeDelta = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6; vlg.padding = new RectOffset(14, 14, 12, 12);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _chatContent;
        }

        void BuildInputBar(RectTransform root)
        {
            var irt = MakePanel(root, "InputBar", PANEL2);
            _inputBar = irt.gameObject;
            SetAnchors(irt, new Vector2(0, 0), new Vector2(1, 0));
            SetOffsets(irt, 8, 8, -8, 72);

            var fieldGo = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(InputField));
            var frt = (RectTransform)fieldGo.transform;
            frt.SetParent(irt, false);
            SetAnchors(frt, new Vector2(0, 0), new Vector2(1, 1));
            SetOffsets(frt, 8, 8, -276, -8);
            fieldGo.GetComponent<Image>().color = Hex("0B0B10");

            var placeholder = MakeText(frt, "Placeholder", "여기에 발언을 입력하세요… (당신 차례에 활성화)", 16, DIM, TextAnchor.MiddleLeft);
            SetOffsets((RectTransform)placeholder.transform, 12, 0, -12, 0);
            var textComp = MakeText(frt, "Text", "", 16, TEXT, TextAnchor.MiddleLeft);
            SetOffsets((RectTransform)textComp.transform, 12, 0, -12, 0);
            textComp.supportRichText = false;

            _input = fieldGo.GetComponent<InputField>();
            _input.textComponent = textComp;
            _input.placeholder = placeholder;
            _input.lineType = InputField.LineType.SingleLine;
            _input.onSubmit.AddListener(delegate { SubmitSpeech(); });

            _pressBtn = MakeButton(irt, "Press", "추궁: 없음", PANEL, CyclePress);
            var prt = (RectTransform)_pressBtn.transform;
            SetAnchors(prt, new Vector2(1, 0), new Vector2(1, 1));
            SetOffsets(prt, -268, 8, -140, -8);
            _pressLabel = _pressBtn.GetComponentInChildren<Text>();

            _sendBtn = MakeButton(irt, "Send", "발언", ACCENT, SubmitSpeech);
            var brt = (RectTransform)_sendBtn.transform;
            SetAnchors(brt, new Vector2(1, 0), new Vector2(1, 1));
            SetOffsets(brt, -132, 8, -8, -8);

            SetInputActive(false, "여기에 발언을 입력하세요… (당신 차례에 활성화)");
        }

        void BuildOverlay(RectTransform root)
        {
            var ort = MakePanel(root, "Overlay", new Color(0, 0, 0, 0.72f));
            _overlay = ort.gameObject;
            SetStretch(ort, 0, 0, 0, 0);

            var box = MakePanel(ort, "Box", PANEL2);
            SetAnchors(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            box.sizeDelta = new Vector2(640, 440);
            box.anchoredPosition = Vector2.zero;

            _overlayTitle = MakeText(box, "Title", "선택", 22, NAMECOL, TextAnchor.UpperCenter);
            SetAnchors((RectTransform)_overlayTitle.transform, new Vector2(0, 1), new Vector2(1, 1));
            SetOffsets((RectTransform)_overlayTitle.transform, 0, -60, 0, -12);

            var listGo = new GameObject("Buttons", typeof(RectTransform));
            _overlayButtons = (RectTransform)listGo.transform;
            _overlayButtons.SetParent(box, false);
            SetStretch(_overlayButtons, 24, 24, -24, -72);
            var vlg = listGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10; vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            _overlay.SetActive(false);
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

        void BuildMapTokens()
        {
            foreach (var kv in _tokens) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _tokens.Clear();
            foreach (var p in _controller.State.Players)
            {
                var go = new GameObject("Token_" + p.Id, typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(_mapPanel, false);
                rt.sizeDelta = new Vector2(64, 64);
                var img = go.GetComponent<Image>();
                img.sprite = _white;
                img.color = p.IsHuman ? Hex("7A3CFF") : Hex("D8B46A");
                var outl = go.AddComponent<Outline>();
                outl.effectColor = p.IsHuman ? Hex("C18CFF") : Color.black;
                outl.effectDistance = new Vector2(2, -2);
                var name = MakeText(rt, "Name", p.Id + (p.IsHuman ? " (YOU)" : ""), 15, TEXT, TextAnchor.MiddleCenter);
                SetAnchors((RectTransform)name.transform, new Vector2(0, 1), new Vector2(1, 1));
                SetOffsets((RectTransform)name.transform, -12, 6, 12, 32);
                _tokens[p.Id] = rt;
            }
        }

        void RefreshMap()
        {
            if (_controller == null || _controller.State == null) return;
            if (_mapPanel == null)
            {
                // 중앙 맵은 월드공간(SpriteMansionView)에서 그려진다. 여기선 상태 텍스트만 갱신.
                BuildPressCandidates();
                RefreshRolePanel();   // 이동 시 "현재 위치" 표시를 실제 방과 동기화
                return;
            }
            foreach (var room in _controller.Rooms)
            {
                if (_roomLabels.TryGetValue(room, out var label))
                {
                    var people = _controller.GetAliveInRoom(room);
                    label.text = room + "  " + people.Count;
                    label.color = (_controller.HumanPlayer != null && _controller.GetPlayerRoom(_controller.HumanPlayer.Id) == room) ? Hex("9BE66D") : NAMECOL;
                }
            }

            var roomBuckets = new Dictionary<string, int>();
            foreach (var p in _controller.State.Players)
            {
                if (!_tokens.TryGetValue(p.Id, out var token)) continue;
                token.gameObject.SetActive(p.Alive);
                if (!p.Alive) continue;
                string room = _controller.GetPlayerRoom(p.Id);
                if (string.IsNullOrEmpty(room) || !_roomRects.TryGetValue(room, out var rr)) continue;
                int idx = roomBuckets.ContainsKey(room) ? roomBuckets[room] : 0;
                roomBuckets[room] = idx + 1;
                token.SetParent(rr, false);
                float x = 0.25f + (idx % 3) * 0.25f;
                float y = 0.35f - (idx / 3) * 0.22f;
                token.anchorMin = token.anchorMax = new Vector2(x, Mathf.Clamp01(y));
                token.anchoredPosition = Vector2.zero;
            }
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
