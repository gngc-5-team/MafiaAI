using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using MafiaAI.Alibi;
using MafiaAI.Alibi.UI;
using MafiaAI.LLM;
using MafiaAI.Mystery.UI;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 「자정의 원고」 — 저택 단면판 진행자.
    /// 탐정이 직접 걸어 다니며(걸음 = 시간) 방을 뒤지고, 그 방에 있는 사람에게 다가가 심문한다.
    /// 진실·판정은 사건 데이터(MysterySession)가, 화면은 뷰가 맡고, 여기서는 입력을 엔진 호출로 바꾸고 결과를 연출한다.
    /// </summary>
    public class ManorGame : MonoBehaviour
    {
        enum Mode { Intro, Explore, Talk, Map, Cutscene, Ended }

        [Header("AI")]
        [SerializeField] GameConfig config = new GameConfig();
        [Tooltip("끄면 직접 묻기는 정해진 얼버무림으로만 답한다(사건 진행에는 영향 없음)")]
        [SerializeField] bool useLlm = true;

        [Header("진행")]
        [Tooltip("타이틀·프롤로그·도입 연출을 건너뛴다(반복 테스트용)")]
        [SerializeField] bool skipIntro = false;

        [Header("월드(씬 저작)")]
        public ManorWorld world;
        public DetectiveController detective;
        public NpcDirector npcs;
        public ManorCamera cameraRig;
        public LightningController lightning;
        public ManorAmbience ambience;
        public SpriteRenderer hoverRing;
        public Transform hoverRingRoot;     // 크기·위치는 이쪽(링 그림은 1×1로 맞춘 자식)
        public GameObject foreground;       // 전경 실루엣(전경이 깊이를 만드는 평소 시점에서만)
        public Vector2 overviewCenter;
        public float overviewSize = 15f;
        public float talkSize = 3.1f;

        [Header("UI(씬 저작)")]
        public ManorHud hud;
        public InterrogationView talk;
        public CanvasGroup bookGroup;
        public CaseBookView book;
        public EvidenceViewer viewer;
        public ReaderView reader;
        public TheoryView theory;
        public MysteryHud cut;
        public AlibiAudio audioFx;
        public RectTransform shakeRoot;

        [Serializable]
        public class SpriteEntry
        {
            public string key;
            public Sprite sprite;
        }

        [Serializable]
        public class CharacterArt
        {
            public string name;
            public Sprite portrait;
            public Sprite face;
        }

        public SpriteEntry[] art;
        public SpriteEntry[] photos;        // 살펴보기 사진(조사 지점 id → 탐정 시선의 사진)
        public CharacterArt[] characters;
        public AudioSource[] npcStepSources; // 손님 발소리(손님 자리에서 난다 — npcs.actors와 같은 순서)

        sealed class Spot
        {
            public string kind;          // look · stair · talk
            public string id;
            public string label;
            public Rect rect;
            public Vector3 anchor;
            public bool done;
            public ManorWorld.StairSlot stair;
        }

        Mode _mode = Mode.Intro;
        MysterySession _session;
        ILlm _llm;
        string _talkTo;
        string _statement;
        bool _busy;
        bool _clicked;
        bool _autoAdvance;
        bool _eventsPending;
        bool _bookOpen;
        bool _lens;                          // E: 이 방에서 살펴볼 수 있는 것에 돋보기 표식
        float _photoUntil;
        float _photoX;
        Dictionary<string, Sprite> _art;
        Dictionary<string, Sprite> _photos;
        readonly List<ManorHud.Marker> _markerList = new List<ManorHud.Marker>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();

        const string ExploreHints = "A D 걷기   ·   클릭 살펴보기   ·   E 돋보기   ·   W S 계단   ·   TAB 수첩   ·   M 저택도";

        // ───────────────────────── 시작 ─────────────────────────

        void Awake()
        {
            _art = new Dictionary<string, Sprite>();
            if (art != null) foreach (var e in art) if (e != null && !string.IsNullOrEmpty(e.key)) _art[e.key] = e.sprite;
            _photos = new Dictionary<string, Sprite>();
            if (photos != null) foreach (var e in photos) if (e != null && !string.IsNullOrEmpty(e.key) && e.sprite != null) _photos[e.key] = e.sprite;
            if (npcs.actors != null && npcStepSources != null)
                for (int i = 0; i < npcs.actors.Length && i < npcStepSources.Length; i++)
                {
                    var a = npcs.actors[i];
                    var src = npcStepSources[i];
                    if (a != null && src != null) a.Step += () => ambience?.NpcStep(a, src);
                }

            detective.Walked += OnWalked;
            detective.CrossedDoor += OnCrossedDoor;
            detective.actor.Step += () => ambience?.Step();
            talk.BackClicked += () => { if (!_busy && _session.Phase != Phase.Confrontation) Run(LeaveTalk()); };
            talk.EvidenceClicked += () => { if (!_busy) ToggleBook(true); };
            talk.StatementClicked += SelectStatement;
            talk.ChipClicked += id => Run(Ask(id));
            talk.QuestionSubmitted += q => Run(FreeAsk(q));
            book.ActClicked += id => Run(Act(id));
            book.InspectClicked += Inspect;
            book.ReadClicked += Read;
            book.ChapterClicked += ReadChapter;
            book.TheoryClicked += OpenTheory;
            viewer.DetailClicked += (ev, d) => Run(FindDetail(ev, d));
            theory.Submitted += () => Run(SubmitTheory());
            cut.RestartClicked += Restart;
        }

        void Start()
        {
            NewSession();
            _ = PrepareAiAsync();
            Run(Intro());
        }

        void OnDestroy() => _cts.Cancel();

        void NewSession()
        {
            _session = new MysterySession(Case01.Build()) { RequirePresence = true };
            var c = _session.Case;
            var start = world.FloorPoint(c.StartRoom, 0.77f);
            detective.Init(c, c.StartRoom, start);
            detective.actor.SetFacing(false);
            npcs.Bind(detective.actor.transform);
            npcs.Sync(_session, true);
            world.RefreshOverlays(_session);
            world.Focus(c.StartRoom, false, 0f);
            world.ShowLabels(false);
            SetNameTags(false);
            cameraRig.SetFloor(world.Room(c.StartRoom).floorY);
            cameraRig.Snap();
            ambience?.SetRoom(c.StartRoom);
            _talkTo = null;
            _statement = null;
            _bookOpen = false;
            if (bookGroup != null) { bookGroup.alpha = 0f; bookGroup.blocksRaycasts = false; bookGroup.gameObject.SetActive(false); }
            talk.HideNow();
            if (hoverRing != null) hoverRing.enabled = false;
            RenderAll();
        }

        async Task PrepareAiAsync()
        {
            if (!useLlm) return;
            bool ok = false;
            try { ok = await OllamaBootstrap.EnsureReadyAsync(config); }
            catch (Exception e) { Debug.LogWarning("[Manor] AI 준비 실패: " + e.Message); }
            if (!ok) return;
            _llm = new OllamaLlm(config);
            try { await _llm.GenerateAsync("", "준비", 0f, false, 1, _cts.Token); }
            catch (Exception e) { Debug.LogWarning("[Manor] 워밍업 실패: " + e.Message); }
        }

        void Restart()
        {
            cut.HideReport();
            NewSession();
            Run(Intro());
        }

        // ───────────────────────── 도입(타이틀 → 프롤로그 → 저택 전경 → 현관) ─────────────────────────

        IEnumerator Intro()
        {
            _mode = Mode.Intro;
            ambience?.SetMode(ManorAmbience.Mode.Cutscene);
            detective.inputEnabled = false;
            hud.ShowChrome(false);
            hud.SetCinema(1f, 0f);
            if (!skipIntro)
            {
                hud.SetFade(1f);
                yield return new WaitForSecondsRealtime(0.4f);
                lightning?.FlashNow(1f);
                yield return hud.Chapter("자정의 원고", "THE  MIDNIGHT  MANUSCRIPT", 2.6f);
                yield return cut.PlayStory(_session.Case.Prologue, Art, null);
                // 폭풍 속 저택 단면 전경 → 현관으로 다가간다
                cameraRig.FrameAt(overviewCenter, overviewSize, 0f);
                world.Focus(_session.Room, true, 0f);
                if (foreground != null) foreground.SetActive(false);
                detective.actor.SetAlpha(0f);
                yield return hud.Fade(0f, 1.2f);
                lightning?.FlashNow(0.9f);
                yield return Wait(2.2f);
                var hall = world.Room(_session.Room);
                cameraRig.SetFloor(hall.floorY);
                yield return cameraRig.FrameAt(new Vector2(hall.box.center.x + hall.box.width * 0.18f, hall.floorY + cameraRig.verticalOffset), cameraRig.exploreSize, 3.2f);
                if (foreground != null) foreground.SetActive(true);
                world.Focus(_session.Room, false, 1f);
                ambience?.Door();
                yield return detective.actor.Fade(1f, 0.6f);
                detective.actor.WalkTo(world.FloorPoint(_session.Room, 0.66f).x, 0.8f);
                while (detective.actor.Walking) yield return null;
                cameraRig.Follow();
                yield return hud.Chapter("제1장  ·  23:45", "안개곶 저택 — 소설가 노아 피살");
            }
            else
            {
                hud.SetFade(0f);
                cameraRig.Follow();
                cameraRig.Snap();
            }
            hud.ShowChrome(true);
            hud.SetCinema(0f, 0.6f);
            yield return PlayLines(new List<Line>
            {
                new Line("하루", "탐, 탐정님…! 위층 서재에… 노아 씨가…"),
                new Line(null, "시신은 2층 서재에 있다. 홀 가운데 계단으로 올라가자."),
            }, null);
            hud.HideSubtitle();
            _mode = Mode.Explore;
            ambience?.SetMode(ManorAmbience.Mode.Explore);
            detective.inputEnabled = true;
            RenderAll();
        }

        // ───────────────────────── 입력 ─────────────────────────

        void Update()
        {
            if (_session == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame) Escape();
            bool advance = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                           || (mouse != null && mouse.leftButton.wasPressedThisFrame && !PointerOverUi());
            if (_busy && advance && !talk.InputFocused) _clicked = true;
            bool keyAdvance = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);
            if (keyAdvance && !talk.InputFocused) cut.Advance();   // 이야기 장면·컷신도 키로 넘긴다
            if (advance) hud.SkipChapter();

            if (_eventsPending && !_busy) { _eventsPending = false; Run(EventsRoutine()); }

            if (!_busy && kb != null && !talk.InputFocused)
            {
                if (kb.tabKey.wasPressedThisFrame && (_mode == Mode.Explore || _mode == Mode.Talk)) ToggleBook(!_bookOpen);
                if (kb.mKey.wasPressedThisFrame && _mode == Mode.Explore && !_bookOpen) Run(OpenMap());
                else if (kb.mKey.wasPressedThisFrame && _mode == Mode.Map) CloseMap();
            }

            if (_mode == Mode.Explore && !_busy && !_bookOpen && !OverlayOpen()) ExploreInput(kb, mouse);
            else { hud.HidePrompt(); if (hoverRing != null && _mode != Mode.Map) hoverRing.enabled = false; }
            if (_mode == Mode.Map && !_busy) MapInput(mouse);

            // 돋보기 표식: 둘러보는 동안만(대화·저택도·컷신에선 숨긴다)
            if (_lens && _mode == Mode.Explore && !_bookOpen && !OverlayOpen()) ShowMarkers();
            else hud.HideMarkers();
            // 사진: 읽을 만큼 지나거나, 그 자리를 떠나거나, 다른 일을 시작하면 치운다
            if (hud.PhotoShown && (Time.time > _photoUntil || _mode != Mode.Explore || _bookOpen
                                   || Mathf.Abs(detective.actor.transform.position.x - _photoX) > 1.6f))
                hud.HidePhoto();
        }

        void ShowMarkers()
        {
            _markerList.Clear();
            foreach (var s in Spots())
            {
                if (s.kind == "look")
                    _markerList.Add(new ManorHud.Marker { world = new Vector3(s.rect.center.x, s.rect.center.y, 0f), kind = 0, done = s.done });
                else if (s.kind == "stair")
                    _markerList.Add(new ManorHud.Marker { world = new Vector3(s.stair.x, s.rect.yMin + 2.9f, 0f), kind = s.stair.up ? 1 : 2, done = false });
            }
            hud.SetMarkers(cameraRig.cam, _markerList);
        }

        void ToggleLens()
        {
            _lens = !_lens;
            if (_lens) ambience?.LensOn(); else ambience?.LensOff();
        }

        bool OverlayOpen() => viewer.Open || reader.Open || theory.Open || cut.StoryOpen || cut.ReportOpen;

        static bool PointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        void Escape()
        {
            if (theory.Open) { if (theory.pickerRoot != null && theory.pickerRoot.activeSelf) theory.pickerRoot.SetActive(false); else theory.Close(); return; }
            if (viewer.Open) { viewer.Close(); return; }
            if (reader.Open) { reader.Close(); return; }
            if (_bookOpen) { ToggleBook(false); return; }
            if (_busy) return;
            if (_mode == Mode.Talk && _session.Phase != Phase.Confrontation) { Run(LeaveTalk()); return; }
            if (_mode == Mode.Map) CloseMap();
        }

        void ExploreInput(Keyboard kb, Mouse mouse)
        {
            var spots = Spots();
            var cam = cameraRig.cam;
            if (kb != null && kb.eKey.wasPressedThisFrame) ToggleLens();

            // 마우스: 가리킨 것에 붉은 동그라미와 이름, 누르면 걸어가서 살핀다(사람이면 말을 건다, 계단이면 오르내린다)
            Spot hover = null;
            if (mouse != null && !PointerOverUi())
            {
                var wp = (Vector2)cam.ScreenToWorldPoint(mouse.position.ReadValue());
                hover = spots.Where(s => s.rect.Contains(wp)).OrderBy(s => s.rect.width * s.rect.height).FirstOrDefault();
                if (hover != null && mouse.leftButton.wasPressedThisFrame) { Run(GoAndUse(hover)); return; }
            }
            // 키보드: 계단 앞에서 W(위) · S(아래)
            float x = detective.actor.transform.position.x;
            var stair = spots.Where(s => s.kind == "stair" && Mathf.Abs(s.stair.x - x) < 1.1f).OrderBy(s => Mathf.Abs(s.stair.x - x)).FirstOrDefault();
            if (kb != null && stair != null)
            {
                bool up = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
                bool down = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
                var want = spots.Where(s => s.kind == "stair" && Mathf.Abs(s.stair.x - x) < 1.1f && ((up && s.stair.up) || (down && !s.stair.up))).FirstOrDefault();
                if (want != null) { Run(Use(want)); return; }
            }

            var shown = hover ?? stair;
            if (shown != null)
            {
                string keyLabel = hover != null ? "클릭" : shown.stair.up ? "W" : "S";
                hud.ShowPrompt(cam, shown.anchor, shown.label, keyLabel, shown.done);
                if (hoverRing != null && hoverRingRoot != null && hover != null)
                {
                    hoverRing.enabled = true;
                    hoverRingRoot.position = new Vector3(shown.rect.center.x, shown.rect.center.y, 0f);
                    hoverRingRoot.localScale = new Vector3(Mathf.Max(0.9f, shown.rect.width * 1.3f), Mathf.Max(0.6f, shown.rect.height * 1.25f), 1f);
                }
                else if (hoverRing != null) hoverRing.enabled = false;
            }
            else
            {
                hud.HidePrompt();
                if (hoverRing != null) hoverRing.enabled = false;
            }
        }

        void MapInput(Mouse mouse)
        {
            if (mouse == null || PointerOverUi()) { hud.HidePrompt(); return; }
            var cam = cameraRig.cam;
            var wp = (Vector2)cam.ScreenToWorldPoint(mouse.position.ReadValue());
            var room = world.rooms.FirstOrDefault(r => r.box.Contains(wp));
            if (room == null) { hud.HidePrompt(); if (hoverRing != null) hoverRing.enabled = false; return; }
            var c = _session.Case;
            bool here = room.id == _session.Room;
            int minutes = here ? 0 : c.TravelMinutes(_session.Room, room.id);
            hud.ShowPrompt(cam, new Vector3(room.box.center.x, room.box.yMax - 0.6f), $"{c.FloorLabel(room.id)} {c.RoomById(room.id).Name}", here ? "지금 여기" : $"{minutes}분 · 클릭", here);
            if (hoverRing != null && hoverRingRoot != null)
            {
                hoverRing.enabled = true;
                hoverRingRoot.position = new Vector3(room.box.center.x, room.box.center.y, 0f);
                hoverRingRoot.localScale = new Vector3(room.box.width * 1.08f, room.box.height * 1.1f, 1f);
            }
            if (mouse.leftButton.wasPressedThisFrame && !here) Run(FastTravel(room.id));
        }

        // ───────────────────────── 지금 방에서 할 수 있는 것 ─────────────────────────

        List<Spot> Spots()
        {
            var list = new List<Spot>();
            var c = _session.Case;
            var room = world.Room(_session.Room);
            if (room == null) return list;
            foreach (var h in _session.VisibleHotspots(_session.Room))
            {
                var slot = room.hotspots?.FirstOrDefault(s => s.id == h.Id);
                if (slot == null) continue;
                list.Add(new Spot { kind = "look", id = h.Id, label = h.Label, rect = slot.rect, anchor = new Vector3(slot.rect.center.x, slot.rect.yMax + 0.35f), done = _session.Examined(h) });
            }
            if (room.stairs != null)
                foreach (var st in room.stairs)
                {
                    var to = c.RoomById(st.to);
                    list.Add(new Spot
                    {
                        kind = "stair", id = "stair:" + st.to, stair = st,
                        label = (st.up ? "▲ 위층 " : "▼ 아래층 ") + to.Name,
                        rect = new Rect(st.x - 0.75f, room.floorY, 1.5f, 3.4f),
                        anchor = new Vector3(st.x, room.floorY + 3.6f),
                    });
                }
            foreach (var name in _session.PeopleIn(_session.Room))
            {
                var a = npcs.Actor(name);
                if (a == null || !a.gameObject.activeSelf) continue;
                var p = a.transform.position;
                if (!room.box.Contains(new Vector2(p.x, room.box.center.y)) || Mathf.Abs(p.y - room.floorY) > 1f)
                    p = world.FloorPoint(_session.Room, _session.LocationX(name));   // 아직 이 방으로 걸어오는 중
                list.Add(new Spot
                {
                    kind = "talk", id = "talk:" + name, label = name + "  — 대화",
                    rect = new Rect(p.x - 0.6f, p.y, 1.2f, 2.3f), anchor = new Vector3(p.x, p.y + 2.55f),
                });
            }
            return list;
        }

        IEnumerator GoAndUse(Spot s)
        {
            var room = world.Room(_session.Room);
            float stand = Mathf.Clamp(s.rect.center.x, room.box.xMin + world.doorMargin, room.box.xMax - world.doorMargin);
            if (s.kind == "talk")
            {
                float px = detective.actor.transform.position.x;
                stand = s.rect.center.x + (px < s.rect.center.x ? -1.3f : 1.3f);
            }
            if (Mathf.Abs(detective.actor.transform.position.x - stand) > 0.12f)
            {
                bool arrived = false;
                detective.AutoWalk(stand, () => arrived = true);
                float t = 0f;
                while (!arrived && t < 8f && detective.AutoWalking) { t += Time.deltaTime; yield return null; }
            }
            yield return Use(s);
        }

        IEnumerator Use(Spot s)
        {
            hud.HidePrompt();
            if (hoverRing != null) hoverRing.enabled = false;
            switch (s.kind)
            {
                case "look": yield return Examine(s.id, s.rect); break;
                case "stair": yield return TakeStairs(s.stair); break;
                case "talk": yield return OpenTalk(s.id.Substring(5)); break;
            }
        }

        // ───────────────────────── 걷기 · 문 · 계단 ─────────────────────────

        void OnWalked(float distance)
        {
            if (_mode != Mode.Explore && _mode != Mode.Intro) return;
            if (_mode == Mode.Intro) return;
            int before = _session.Clock;
            var r = _session.Walk(distance / world.roomWidth);
            if (_session.Clock != before) OnClockChanged();
            Quick(r);
        }

        void OnCrossedDoor(string roomId)
        {
            var r = _session.EnterRoom(roomId);
            if (r.Outcome == Outcome.Refused) return;
            ambience?.Door();
            EnteredRoom(r);
            Quick(r);
        }

        void EnteredRoom(ActionResult r)
        {
            world.Focus(_session.Room, false);
            cameraRig.SetFloor(world.Room(_session.Room).floorY);
            ambience?.SetRoom(_session.Room);
            hud.SetRoom(_session);
            var intro = r.Lines.FirstOrDefault(l => l.Speaker == null);
            if (intro != null) hud.Narrate(intro.Text);
            RenderAll();
        }

        IEnumerator TakeStairs(ManorWorld.StairSlot st)
        {
            string from = _session.Room;
            detective.inputEnabled = false;
            var a = detective.actor;
            if (Mathf.Abs(a.transform.position.x - st.x) > 0.1f)
            {
                bool arrived = false;
                detective.AutoWalk(st.x, () => arrived = true);
                float t = 0f;
                while (!arrived && t < 6f) { t += Time.deltaTime; yield return null; }
            }
            yield return a.Fade(0f, 0.25f);
            var r = _session.UseStairs(st.to);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield return a.Fade(1f, 0.2f); detective.inputEnabled = true; yield break; }
            var to = world.Room(st.to);
            float arrive = to.stairs?.FirstOrDefault(s => s.to == from)?.x ?? to.box.center.x;
            detective.SetRoom(st.to, new Vector3(arrive, to.floorY, 0f));
            string stairFloor = ManorAmbience.StairFloor(from, st.to);
            for (int i = 0; i < 4; i++) { ambience?.Step(stairFloor); yield return new WaitForSeconds(0.17f); }
            EnteredRoom(r);
            OnClockChanged();
            yield return a.Fade(1f, 0.3f);
            detective.inputEnabled = true;
            yield return After(r);
        }

        IEnumerator FastTravel(string roomId)
        {
            var r = _session.Move(roomId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            yield return hud.Fade(1f, 0.35f);
            CloseMap();
            detective.SetRoom(roomId, world.FloorPoint(roomId, 0.5f));
            cameraRig.SetFloor(world.Room(roomId).floorY);
            cameraRig.Snap();
            EnteredRoom(r);
            OnClockChanged();
            yield return hud.Fade(0f, 0.35f);
            yield return After(r);
        }

        IEnumerator OpenMap()
        {
            _mode = Mode.Map;
            ambience?.SetMode(ManorAmbience.Mode.Map);
            detective.inputEnabled = false;
            detective.Halt();
            hud.HidePrompt();
            world.Focus(_session.Room, true);
            world.ShowLabels(true);
            SetNameTags(true);
            if (foreground != null) foreground.SetActive(false);
            hud.SetHints("방을 누르면 그리로 간다(걸린 시간만큼 시계가 간다)   ·   M 닫기");
            hud.SetObjective("저택 단면도 — 손님들이 지금 있는 곳");
            yield return cameraRig.FrameAt(overviewCenter, overviewSize, 0.8f);
        }

        void CloseMap()
        {
            _mode = Mode.Explore;
            ambience?.SetMode(ManorAmbience.Mode.Explore);
            world.Focus(_session.Room, false);
            world.ShowLabels(false);
            SetNameTags(false);
            if (foreground != null) foreground.SetActive(true);
            cameraRig.Follow();
            detective.inputEnabled = true;
            hud.HidePrompt();
            if (hoverRing != null) hoverRing.enabled = false;
            RenderAll();
        }

        void SetNameTags(bool on)
        {
            if (npcs.actors != null) foreach (var a in npcs.actors) if (a != null && a.nameTag != null) a.nameTag.SetActive(on);
            if (detective.actor.nameTag != null) detective.actor.nameTag.SetActive(on);
        }

        // ───────────────────────── 조사 ─────────────────────────

        IEnumerator Examine(string hotspotId, Rect rect)
        {
            detective.actor.Face(rect.center.x);
            var r = _session.Examine(hotspotId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            var h = _session.CurrentRoom.Hotspots.First(x => x.Id == hotspotId);
            string text = string.Join(" ", r.Lines.Select(l => l.Text));
            hud.Narrate(text);
            if (_photos != null && _photos.TryGetValue(hotspotId, out var photo))
            {
                // 물건이 화면 왼쪽이면 사진은 오른쪽에(탐정과 물건을 가리지 않게)
                bool right = cameraRig.cam.WorldToViewportPoint(new Vector3(rect.center.x, rect.center.y, 0f)).x < 0.5f;
                hud.ShowPhoto(photo, h.Label, _session.ClockLabel, right);
                ambience?.Photo();
                _photoUntil = Time.time + 3.2f + text.Length * 0.06f;
                _photoX = detective.actor.transform.position.x;
            }
            else audioFx?.Click();
            hud.Toast("◆ " + h.Label);
            if (r.Gained.Count > 0) audioFx?.Stamp();
            OnClockChanged();
            world.RefreshOverlays(_session);
            yield return After(r);
        }

        // ───────────────────────── 심문 ─────────────────────────

        IEnumerator OpenTalk(string name)
        {
            if (_session.IsDead(name)) { hud.Toast(name + " — 더 이상 말할 수 없다."); yield break; }
            var who = _session.Case.SuspectByName(name);
            var actor = npcs.Actor(name);
            if (who == null || actor == null) yield break;
            npcs.Settle(name, _session);
            _mode = Mode.Talk;
            ambience?.SetMode(ManorAmbience.Mode.Talk);
            _talkTo = name;
            _statement = null;
            _session.Hold(name);
            detective.inputEnabled = false;
            detective.Halt();
            detective.actor.Face(actor.transform.position.x);
            actor.Face(detective.actor.transform.position.x);
            audioFx?.Click();
            hud.SetCinema(1f);
            FrameTwoShot(actor);
            talk.SetConfront(false);
            RenderTalk();
            yield return talk.Show(who, Character(name)?.portrait);
            var r = _session.Meet(name);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield return LeaveTalk(); yield break; }
            if (r.Lines.Count > 0) yield return PlayLines(r.Lines, null);
            else hud.Say(name, who.Bio, false);
            yield return After(r);
        }

        void FrameTwoShot(PixelActor other)
        {
            var a = detective.actor.transform.position;
            var b = other.transform.position;
            float mid = (a.x + b.x) * 0.5f;
            cameraRig.FrameAt(new Vector2(mid, a.y + talkSize * 0.55f), talkSize, 0.7f);
        }

        IEnumerator LeaveTalk()
        {
            if (_bookOpen) ToggleBook(false);
            yield return talk.Hide();
            hud.HideSubtitle();
            hud.SetCinema(0f);
            _session.Release(_talkTo);
            _talkTo = null;
            _statement = null;
            _mode = Mode.Explore;
            ambience?.SetMode(ManorAmbience.Mode.Explore);
            cameraRig.Follow();
            detective.inputEnabled = true;
            npcs.Sync(_session, false);   // 붙잡혀 있던 사이 갈 곳이 생겼으면 이제 간다
            RenderAll();
        }

        void SelectStatement(string id)
        {
            if (_busy || _talkTo == null) return;
            audioFx?.Click();
            _statement = _statement == id ? null : id;
            book.SetMode(_statement != null ? CaseBookView.ActMode.Present : CaseBookView.ActMode.Show);
            RenderTalk();
            if (_statement != null)
            {
                var st = _session.Statements(_talkTo).FirstOrDefault(x => x.Id == _statement);
                hud.Say(null, $"「{st?.Text}」 — 여기에 맞설 증거는?", false);
                book.SetTab(0);
                ToggleBook(true);
            }
        }

        IEnumerator Ask(string topicId)
        {
            if (_talkTo == null) yield break;
            audioFx?.Click();
            var r = _session.Ask(_talkTo, topicId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            yield return PlayLines(r.Lines.Where(l => l.Speaker != "탐정").ToList(), null);
            OnClockChanged();
            yield return After(r);
        }

        IEnumerator FreeAsk(string question)
        {
            if (_talkTo == null || _session.IsDead(_talkTo)) yield break;
            string name = _talkTo;
            if (_session.IsClosed(name)) { hud.Say(name, _session.Case.SuspectByName(name).ClosedLine); yield break; }
            talk.SetInputEnabled(false);
            hud.Say("탐정", question, false);
            yield return Wait(0.6f);
            hud.Say(name, "……");
            var task = MysteryTalk.AnswerAsync(useLlm ? _llm : null, _session, name, question, _cts.Token);
            while (!task.IsCompleted) yield return null;
            string answer = task.Status == TaskStatus.RanToCompletion ? task.Result : MysteryTalk.Deflect(name);
            var r = _session.RecordFreeQuestion(name, question, answer);
            hud.Say(name, answer);
            talk.SetInputEnabled(true);
            OnClockChanged();
            yield return After(r);
        }

        IEnumerator Act(string evidenceId)
        {
            if (_session.Phase == Phase.Confrontation) { yield return Rebut(evidenceId); yield break; }
            if (_talkTo == null) { hud.Toast("증거는 누군가와 마주 선 채로 내민다."); yield break; }
            ToggleBook(false);
            if (_statement == null)
            {
                audioFx?.Click();
                var r = _session.Show(_talkTo, evidenceId);
                if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
                yield return PlayLines(r.Lines.Where(l => l.Speaker != "탐정").ToList(), null);
                OnClockChanged();
                yield return After(r);
                yield break;
            }

            var res = _session.Present(_talkTo, _statement, evidenceId);
            if (res.Outcome == Outcome.Refused) { hud.Toast(res.Message); yield break; }
            var said = res.Lines.Where(l => l.Speaker != "탐정").ToList();
            hud.Say("탐정", $"그렇다면 이건 어떻게 설명하시겠습니까 — {_session.Case.Ev(evidenceId).Name}.", false);
            yield return Wait(0.9f);
            if (res.Breakthrough)
            {
                audioFx?.Stamp();
                audioFx?.Duck(true);
                lightning?.FlashNow(0.6f);
                yield return cut.PlayObjection("모순!", res.Why, shakeRoot);
                audioFx?.Duck(false);
                StartCoroutine(talk.PlayBreak(shakeRoot));
                cameraRig.Shake(0.08f, 0.4f);
                _statement = null;
                book.SetMode(CaseBookView.ActMode.Show);
                RenderTalk();
                yield return PlayLines(said, null);
                if (res.Gained.Count > 0) audioFx?.Reveal();
            }
            else
            {
                StartCoroutine(talk.PlayWrong());
                yield return PlayLines(said, null);
                if (!string.IsNullOrEmpty(res.Message)) hud.Toast(res.Message);
            }
            OnClockChanged();
            yield return After(res);
        }

        IEnumerator Rebut(string evidenceId)
        {
            ToggleBook(false);
            var r = _session.Rebut(evidenceId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            if (r.Breakthrough)
            {
                audioFx?.Stamp();
                audioFx?.Duck(true);
                lightning?.FlashNow(0.8f);
                yield return cut.PlayObjection(_session.Phase == Phase.Ended ? "자백" : "반박!", null, shakeRoot);
                audioFx?.Duck(false);
                StartCoroutine(talk.PlayBreak(shakeRoot));
            }
            else StartCoroutine(talk.PlayWrong());
            talk.SetConfront(_session.Phase == Phase.Confrontation, _session.ResolveLeft);
            yield return PlayLines(r.Lines, null);
            if (!string.IsNullOrEmpty(r.Message)) hud.Toast(r.Message);
            if (_session.Phase == Phase.Investigation)
            {
                // 추궁이 막혔다 — 다시 수사로
                npcs.Unstage();
                npcs.Sync(_session, true);
                yield return LeaveTalk();
            }
            else if (_session.Phase == Phase.Confrontation) { book.SetMode(CaseBookView.ActMode.Rebut); ToggleBook(true); }
            yield return After(r);
        }

        // ───────────────────────── 수첩 · 증거 · 원고 · 재구성 ─────────────────────────

        void ToggleBook(bool open)
        {
            if (bookGroup == null || open == _bookOpen) return;
            _bookOpen = open;
            if (open)
            {
                if (_mode == Mode.Explore) book.SetMode(CaseBookView.ActMode.None);
                else if (_mode == Mode.Talk && _session.Phase == Phase.Confrontation) book.SetMode(CaseBookView.ActMode.Rebut);
                else if (_mode == Mode.Talk) book.SetMode(_statement != null ? CaseBookView.ActMode.Present : CaseBookView.ActMode.Show);
                book.Render(_session, Art);
                bookGroup.gameObject.SetActive(true);
                bookGroup.blocksRaycasts = true;
                StartCoroutine(AlibiFx.Fade(bookGroup, bookGroup.alpha, 1f, 0.2f));
                if (_mode == Mode.Talk) talk.SetPortraitVisible(false);
                detective.inputEnabled = false;
                hud.HidePrompt();
                audioFx?.Click();
            }
            else
            {
                bookGroup.blocksRaycasts = false;
                StartCoroutine(FadeOutBook());
                if (_mode == Mode.Talk) talk.SetPortraitVisible(true);
                if (_mode == Mode.Explore) detective.inputEnabled = true;
            }
        }

        IEnumerator FadeOutBook()
        {
            yield return AlibiFx.Fade(bookGroup, bookGroup.alpha, 0f, 0.18f);
            if (!_bookOpen) bookGroup.gameObject.SetActive(false);
        }

        void Inspect(string evidenceId)
        {
            var e = _session.Case.Ev(evidenceId);
            if (e == null) return;
            audioFx?.Click();
            viewer.Show(_session, e, Art(e.Image));
        }

        IEnumerator FindDetail(string evidenceId, string detailId)
        {
            var r = _session.InspectDetail(evidenceId, detailId);
            if (r.Outcome == Outcome.Refused) yield break;
            viewer.Refresh(_session, _session.Case.Ev(evidenceId));
            if (r.DetailsFound.Count > 0)
            {
                audioFx?.Reveal();
                var d = _session.Case.Ev(evidenceId).Details.First(x => x.Id == detailId);
                hud.Toast("단서 발견 — " + d.Label);
            }
            yield return After(r);
        }

        void Read(string evidenceId)
        {
            var e = _session.Case.Ev(evidenceId);
            if (e == null || string.IsNullOrEmpty(e.Document)) return;
            audioFx?.Click();
            reader.Show(e.Name, e.Document, e.Source);
        }

        void ReadChapter(int chapter)
        {
            var have = _session.FoundEvidence.Where(e => e.Chapter == chapter && !string.IsNullOrEmpty(e.Document)).ToList();
            var e = have.FirstOrDefault(x => x.Id.StartsWith("ch")) ?? have.FirstOrDefault();
            if (e != null) Read(e.Id);
        }

        void OpenTheory()
        {
            if (_busy) return;
            audioFx?.Click();
            if (_session.Has("theory_ok") && _session.Phase == Phase.Investigation)
            {
                var r = _session.ResumeConfrontation();
                ToggleBook(false);
                Run(BeginConfrontation(r));
                return;
            }
            theory.Show(_session);
        }

        IEnumerator SubmitTheory()
        {
            var r = _session.SubmitTheory(theory.Answers.ToDictionary(k => k.Key, k => k.Value));
            if (r.Outcome == Outcome.Refused) { theory.SetFeedback(r.Message); yield break; }
            if (r.Outcome != Outcome.Correct)
            {
                theory.SetFeedback($"{r.Message}  <color=#9E1B30>({_session.Case.WrongTheoryCost}분이 흘렀다)</color>");
                theory.Show(_session);
                OnClockChanged();
                yield return After(r);
                yield break;
            }
            theory.Close();
            ToggleBook(false);
            audioFx?.Reveal();
            yield return BeginConfrontation(r);
        }

        const string ConfrontStage = "drawing";

        /// <summary>화면 아래에 보일 방 — 최후의 추궁 동안엔 모두가 모인 응접실.</summary>
        string ShownRoom => _session.Phase == Phase.Confrontation ? ConfrontStage : null;

        /// <summary>모두를 응접실에 모으고 범인과 마주 선다.</summary>
        IEnumerator BeginConfrontation(ActionResult r)
        {
            string killer = _session.Case.Killer;
            if (_mode == Mode.Talk) { yield return talk.Hide(); }
            _mode = Mode.Cutscene;
            detective.inputEnabled = false;
            yield return hud.Fade(1f, 0.7f);
            const string stage = ConfrontStage;
            var room = world.Room(stage);
            detective.SetRoom(stage, world.FloorPoint(stage, 0.2f));
            detective.actor.SetFacing(true);
            var living = _session.Case.Suspects.Select(s => s.Name).Where(n => !_session.IsDead(n)).ToList();
            living.Remove(killer);
            float[] spots = { 0.62f, 0.74f, 0.86f, 0.5f };
            for (int i = 0; i < living.Count; i++) npcs.Stage(living[i], world.FloorPoint(stage, spots[i % spots.Length]), false);
            npcs.Stage(killer, world.FloorPoint(stage, 0.36f), false);
            world.Focus(stage, false, 0f);
            cameraRig.SetFloor(room.floorY);
            hud.SetRoom(_session, ShownRoom);
            var zero = npcs.Actor(killer);
            _talkTo = killer;
            _statement = null;
            _mode = Mode.Talk;
            ambience?.SetMode(ManorAmbience.Mode.Talk);
            FrameTwoShot(zero);
            hud.SetCinema(1f, 0f);
            yield return hud.Fade(0f, 0.8f);
            yield return hud.Chapter("최후의 추궁", "모두가 응접실에 모였다");
            talk.SetConfront(true, _session.ResolveLeft);
            RenderTalk();
            yield return talk.Show(_session.Case.SuspectByName(killer), Character(killer)?.portrait);
            yield return PlayLines(r.Lines, null);
            book.SetMode(CaseBookView.ActMode.Rebut);
            book.SetTab(0);
            ToggleBook(true);
            yield return After(r);
        }

        // ───────────────────────── 결과 처리 · 시간 사건 ─────────────────────────

        void OnClockChanged()
        {
            hud.SetClock(_session);
            npcs.Sync(_session, false);
            world.RefreshOverlays(_session);

        }

        /// <summary>걷다가·문을 지나다 생긴 일 — 알림만 띄우고, 컷신이 필요하면 다음 프레임에 처리한다.</summary>
        /// <summary>걷다가·문을 지나다 생긴 일 — 알림만 띄운다. 컷신이 필요한 사건이 실제로 있을 때만 다음 프레임에 처리한다
        /// (예전엔 걸음마다 처리 루틴을 돌려 한 프레임씩 입력이 막혔다 → 걸음이 반 속도로 끊기고 걷기 애니메이션이 매번 처음부터 다시 돌았다).</summary>
        void Quick(ActionResult r)
        {
            foreach (var n in r.Notices) hud.Toast(n);
            if (r.Gained.Count > 0 || r.Notices.Count > 0) RenderAll();
            if (_session.HasPendingEvents) _eventsPending = true;
        }

        IEnumerator After(ActionResult r)
        {
            if (r.Gained.Count > 0)
            {
                var first = _session.Case.Ev(r.Gained[0]);
                string head = first.Kind == EvidenceKind.Testimony ? "증언 기록 — " : first.Kind == EvidenceKind.Document ? "수첩에 끼움 — " : "증거 확보 — ";
                hud.Toast(head + first.Name + (r.Gained.Count > 1 ? $"  외 {r.Gained.Count - 1}건" : ""));
                book.Select(r.Gained.Last());
            }
            foreach (var n in r.Notices) hud.Toast(n);
            RenderAll();
            yield return Events();
            if (_session.Phase == Phase.Ended && _mode != Mode.Ended) yield return Ending();
        }

        IEnumerator EventsRoutine()
        {
            yield return Events();
            if (_session.Phase == Phase.Ended && _mode != Mode.Ended) yield return Ending();
        }

        IEnumerator Events()
        {
            while (_session.TryTakeEvent(out var ev))
            {
                var e = ev.Event;
                if (ev.Prevented) { hud.Toast(e.PreventedToast); continue; }
                if (e.Id == "midnight")
                {
                    var prevMode = _mode;
                    bool inputWas = detective.inputEnabled;
                    detective.inputEnabled = false;
                    detective.Halt();
                    if (ambience != null) StartCoroutine(ambience.Chime(4));
                    yield return hud.Chapter("자정", "괘종시계가 열두 번 운다 — 노아가 '이야기를 끝내겠다'던 시각", 2.4f);
                    detective.inputEnabled = inputWas && prevMode == Mode.Explore;
                    continue;
                }
                if (!string.IsNullOrEmpty(e.CutTitle))
                {
                    detective.inputEnabled = false;
                    detective.Halt();
                    if (_bookOpen) ToggleBook(false);
                    lightning?.FlashNow(1f);
                    audioFx?.Murder();
                    audioFx?.Duck(true);
                    yield return cut.PlayMurder(e.CutTitle, e.CutSub, e.CutNote);
                    audioFx?.Duck(false);
                    npcs.Sync(_session, true);
                    world.RefreshOverlays(_session);
                    if (_mode == Mode.Talk && _talkTo != null && _session.IsDead(_talkTo)) yield return LeaveTalk();
                    if (_mode == Mode.Explore) detective.inputEnabled = true;
                    hud.SetObjective("온실로 — 세이에게 무슨 일이");
                }
                else if (!string.IsNullOrEmpty(e.Toast)) hud.Narrate(e.Toast);
            }
            RenderAll();
        }

        IEnumerator Ending()
        {
            _mode = Mode.Ended;
            ambience?.SetMode(ManorAmbience.Mode.Cutscene);
            detective.inputEnabled = false;
            if (_bookOpen) ToggleBook(false);
            var c = _session.Case;
            var ending = c.Endings.FirstOrDefault(e => e.Id == _session.EndingId);
            yield return Wait(0.8f);
            yield return talk.Hide();
            audioFx?.Ending();
            if (ending != null) yield return cut.PlayStory(ending.Panels, Art, ending.Title);
            yield return cut.ShowReport(_session.Solved, (_session.Solved ? "사건 보고서 — " : "사건 보고서 — 미제 · ") + (ending?.Title ?? ""), Report());
        }

        string Report()
        {
            var c = _session.Case;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(_session.Solved ? $"<b>범인</b>  {c.Killer} — 의사. 원고 6장에 적힌 3년 전 수술 사고를 감추려 했다." : "<b>범인</b>  밝혀지지 않았다. 진실은 이랬다 —");
            sb.AppendLine();
            sb.AppendLine("<b>그날 밤의 실제 경위</b>");
            foreach (var raw in c.Truth)
            {
                string line = raw;
                if (line.StartsWith("["))
                {
                    int close = line.IndexOf(']');
                    if (!_session.Eval(line.Substring(1, close - 1))) continue;
                    line = line.Substring(close + 1);
                }
                sb.AppendLine("· " + line);
            }
            sb.AppendLine();
            int lies = c.AllStatements.Count(x => x.St.Lie);
            int broken = c.AllStatements.Count(x => x.St.Lie && _session.Broken(x.St));
            sb.AppendLine($"<b>수사 기록</b>  끝난 시각 {_session.ClockLabel} · 찾은 증거 {_session.Found.Count}/{c.Evidence.Length} · 무너뜨린 거짓말 {broken}/{lies} · 헛짚은 제시 {_session.WrongPresents}번 · 재구성 {_session.TheoryAttempts}번");
            sb.AppendLine(_session.IsDead("세이") ? "세이를 지키지 못했다." : "세이는 무사하다.");
            return sb.ToString();
        }

        // ───────────────────────── 대사 재생 ─────────────────────────

        /// <summary>자막을 한 줄씩 흘린다(클릭·스페이스로 넘김). 마지막 줄은 기다리지 않는다.</summary>
        IEnumerator PlayLines(IList<Line> lines, string asked)
        {
            if (lines == null || lines.Count == 0) yield break;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                hud.Say(l.Speaker, l.Text);
                if (i == lines.Count - 1) break;
                _clicked = false;
                float t = 0f;
                while (true)
                {
                    t += Time.unscaledDeltaTime;
                    if (_autoAdvance && !hud.Typing && t > 0.3f) break;
                    if (_clicked && !hud.Typing) break;
                    if (_clicked && hud.Typing) { hud.CompleteTyping(); _clicked = false; }
                    yield return null;
                }
            }
        }

        // ───────────────────────── 그리기 ─────────────────────────

        void RenderAll()
        {
            if (_session == null) return;
            hud.SetClock(_session);
            hud.SetRoom(_session, ShownRoom);
            if (_mode != Mode.Map) hud.SetObjective(Objective());
            hud.SetHints(_mode == Mode.Talk ? "진술을 눌러 증거로 맞서기   ·   TAB 수첩   ·   ESC 물러나기"
                        : _mode == Mode.Map ? "방을 누르면 그리로 간다   ·   M 닫기"
                        : ExploreHints);
            if (_bookOpen) book.Render(_session, Art);
            if (_mode == Mode.Talk) RenderTalk();
        }

        string Objective()
        {
            if (!_session.Visited("study")) return "2층 서재로 — 홀 가운데 계단";
            if (_session.Has("theory_ok")) return "제로를 몰아붙여라";
            if (_session.IsDead("세이") && !_session.HasEvidence("notebook")) return "온실 — 세이에게 무슨 일이";
            int warn = MysteryCase.At(1, 45), murder = MysteryCase.At(2, 0);
            if (_session.Clock >= warn && _session.Clock < murder && !_session.IsDead("세이")) return "세이가 온실로 갔다 — 혼자 두면 위험하다";
            var q = _session.OpenQuestions.FirstOrDefault();
            if (q != null) return q.Text;
            return "사건 재구성 — 수첩(TAB)에서";
        }

        void RenderTalk()
        {
            if (_talkTo == null) return;
            talk.RenderStatements(_session, _talkTo, _statement);
            bool confront = _session.Phase == Phase.Confrontation;
            var chips = confront ? Enumerable.Empty<(string, string, bool)>()
                : _session.Topics(_talkTo).Select(t => (t.Id, t.Label, _session.Asked(t)));
            talk.RenderChips(chips);
            talk.SetInputEnabled(!confront && !_busy);
        }

        // ───────────────────────── 도우미 ─────────────────────────

        void Run(IEnumerator routine)
        {
            if (_busy || routine == null) return;
            StartCoroutine(Guard(routine));
        }

        IEnumerator Guard(IEnumerator routine)
        {
            _busy = true;
            bool inputWas = detective.inputEnabled;
            if (_mode == Mode.Explore) detective.inputEnabled = false;
            yield return routine;
            _busy = false;
            detective.inputEnabled = _mode == Mode.Explore && !_bookOpen;
            if (_mode == Mode.Talk) talk.SetInputEnabled(_session.Phase == Phase.Investigation);
        }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        Sprite Art(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key.StartsWith("face:")) return Character(key.Substring(5))?.face;
            return _art != null && _art.TryGetValue(key, out var s) ? s : null;
        }

        CharacterArt Character(string name) => characters?.FirstOrDefault(c => c.name == name);

        // ───────────────────────── 자동 검증용 ─────────────────────────

        public MysterySession EditorSession => _session;
        public bool EditorBusy => _busy || _mode == Mode.Intro;
        public string EditorMode => _mode.ToString();
        public void EditorAutoAdvance(bool on) => _autoAdvance = on;
        public void EditorClick() => _clicked = true;

        /// <summary>방 안에서 u(0~1) 자리까지 실제로 걸어간다.</summary>
        public void EditorWalkTo(float u)
        {
            var p = world.FloorPoint(_session.Room, u);
            detective.AutoWalk(p.x, null, 1f);
        }

        /// <summary>같은 층의 다른 방까지 문을 지나 실제로 걸어간다.</summary>
        public void EditorWalkToRoom(string roomId, float u)
        {
            var p = world.FloorPoint(roomId, u);
            detective.AutoWalk(p.x, null, 1f);
        }

        public bool EditorWalking => detective.AutoWalking;
        public void EditorLens(bool on) { if (_lens != on) ToggleLens(); }
        public bool EditorPhotoShown => hud.PhotoShown;
        public void EditorFlash() => lightning?.FlashNow(1f);
        public void EditorUse(string id)
        {
            var s = Spots().FirstOrDefault(x => x.id == id);
            if (s != null) Run(GoAndUse(s));
            else Debug.LogWarning("[Manor] 지금 방에 없음: " + id);
        }
        public void EditorTravel(string roomId) => Run(FastTravel(roomId));
        public void EditorMap(bool open) { if (open) Run(OpenMap()); else CloseMap(); }
        public void EditorBook(bool open) => ToggleBook(open);
        public void EditorSelectStatement(string id) { _statement = null; SelectStatement(id); }
        public void EditorAct(string evidenceId) => Run(Act(evidenceId));
        public void EditorAsk(string topicId) => Run(Ask(topicId));
        public void EditorFreeAsk(string q) => Run(FreeAsk(q));
        public void EditorLeaveTalk() => Run(LeaveTalk());
        public void EditorInspect(string evidenceId) => Inspect(evidenceId);
        public void EditorDetail(string evidenceId, string detailId) => Run(FindDetail(evidenceId, detailId));
        public void EditorRead(string evidenceId) => Read(evidenceId);
        public void EditorCloseOverlays() { if (viewer.Open) viewer.Close(); if (reader.Open) reader.Close(); if (theory.Open) theory.Close(); }
        public void EditorTheory(IDictionary<string, string> answers) { theory.Show(_session); theory.Fill(answers); }
        public void EditorSubmitTheory() => Run(SubmitTheory());
        public void EditorSelectEvidence(string id) { book.SetTab(0); book.Select(id); }
        public void EditorSkipChapter() => hud.SkipChapter();
    }
}
