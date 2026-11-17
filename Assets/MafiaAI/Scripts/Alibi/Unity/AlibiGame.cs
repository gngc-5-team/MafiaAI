using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using MafiaAI.LLM;

namespace MafiaAI.Alibi.UI
{
    /// <summary>
    /// 알리바이 심문의 진행자. 엔진(AlibiSession)과 씬 저작 뷰를 잇는다.
    /// 판정·진실은 엔진이, 말투는 LLM이, 화면은 뷰가 맡는다 — 이 스크립트는 클릭을 엔진 호출로 바꾸고, 결과를 연출로 보여 줄 뿐이다.
    /// </summary>
    public class AlibiGame : MonoBehaviour
    {
        [Header("AI")]
        [SerializeField] GameConfig config = new GameConfig();
        [Tooltip("끄면 규칙 해석 + 템플릿 대사만으로 진행한다(AI 없이도 재밌는지 비교용)")]
        [SerializeField] bool useLlm = true;
        [Tooltip("상단에 AI가 질문을 어떻게 알아들었는지 작게 표시")]
        [SerializeField] bool showDebug = false;   // 켜면 상단 오른쪽에 질문 해석·대사 출처가 뜬다

        [Header("사건")]
        [Tooltip("0이면 매번 새 사건. 같은 사건을 다시 하려면 사건 번호를 넣는다")]
        [SerializeField] int fixedSeed = 0;
        [Range(0f, 1f)]
        [Tooltip("범인의 모순이 '비밀 고백' 뒤에야 드러나는 어려운 사건 비율")]
        [SerializeField] float chainChance = 0.3f;
        [SerializeField] int secondMurderAt = 60;   // 23:45부터 분 — 00:45
        [SerializeField] int deadlineAt = 120;      // 01:45

        [Header("뷰(씬 저작)")]
        public AlibiStageView stage;
        public AlibiDialogueView dialogue;
        public AlibiNotebookView notebook;
        public AlibiHud hud;
        public AlibiAudio audioFx;
        public RectTransform shakeRoot;     // 모순! 순간 흔들 화면 전체

        [Serializable]
        public class CharacterArt
        {
            public string name;
            public Sprite card;       // 액자 카드(목록·지목)
            public Sprite portrait;   // 전신(심문)
            public Sprite face;       // 얼굴 메달(수첩)
        }
        public CharacterArt[] characters;

        AlibiSession _session;
        ILlm _llm;
        bool _aiReady;
        readonly List<CellRef> _selected = new List<CellRef>();
        int _focusSlot = AlibiCase.MurderSlot;
        string _open;
        bool _busy;
        string _pendingAccuse;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();

        void Awake()
        {
            stage.CardClicked += i => OpenSuspect(_session?.Case.Suspects[i].Name);
            stage.BackClicked += BackToLineup;
            notebook.RowClicked += i => OpenSuspect(_session?.Case.Suspects[i].Name);
            notebook.CellClicked += OnCellClicked;
            notebook.SlotClicked += t => { _focusSlot = t; audioFx?.Click(); RenderAll(); };
            notebook.PresentClicked += OnPresent;
            notebook.ClearClicked += () => { _selected.Clear(); RenderAll(); };
            notebook.AccuseClicked += () => OpenAccuse(false);
            dialogue.QuestionSubmitted += OnQuestion;
            dialogue.PressClicked += OnPress;
            hud.StartClicked += () => { audioFx?.Click(); hud.HideBriefing(); RenderAll(); };
            hud.AccuseCancelled += () => { hud.HideAccuse(); RenderAll(); };
            hud.PickClicked += i => { _pendingAccuse = _session.Case.Suspects[i].Name; audioFx?.Click(); hud.AskConfirm(_pendingAccuse); };
            hud.ConfirmAnswered += yes => { if (yes) Accuse(_pendingAccuse); else hud.HideConfirm(); };
            hud.NewCaseClicked += NewCase;
        }

        async void Start()
        {
            NewCase();
            await PrepareAiAsync();
        }

        void OnDestroy() => _cts.Cancel();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (dialogue.HistoryOpen) { dialogue.HideHistory(); return; }
            if (hud.AccuseOpen && !hud.AccuseForced) { hud.HideAccuse(); RenderAll(); return; }
            if (_open != null && !_busy) BackToLineup();
        }

        // ───────────── 준비 ─────────────

        void NewCase()
        {
            int seed = fixedSeed != 0 ? fixedSeed : UnityEngine.Random.Range(1, 100000);
            var c = CaseGenerator.Generate(seed, new CaseGenerator.Options { ChainChance = chainChance });
            _session = new AlibiSession(c, _llm, Options());
            _selected.Clear();
            _open = null;
            _focusSlot = AlibiCase.MurderSlot;
            hud.HideResult();
            hud.HideAccuse();
            stage.ShowLineup();
            dialogue.ShowNarration($"{Josa.IGa(c.Victim)} {c.MurderRoom}에서 숨진 채 발견됐다. 다섯 사람 가운데 누군가가 거짓말을 하고 있다.");
            hud.ShowBriefing(_session.Briefing(), _aiReady);
            RenderAll();
        }

        SessionOptions Options() => new SessionOptions { UseLlm = useLlm, SecondMurderAt = secondMurderAt, DeadlineAt = deadlineAt };

        async Task PrepareAiAsync()
        {
            if (!useLlm) hud.SetAi("AI 꺼짐 · 정해진 대사로 진행");
            else
            {
                hud.SetAi("AI 확인 중…");
                bool ok = false;
                try { ok = await OllamaBootstrap.EnsureReadyAsync(config); }
                catch (Exception e) { Debug.LogWarning("[Alibi] AI 준비 실패: " + e.Message); }
                if (ok)
                {
                    _llm = new OllamaLlm(config);
                    hud.SetAi("AI 깨우는 중…");
                    try { await _llm.GenerateAsync("", "준비", 0f, false, 1, _cts.Token); }  // 첫 질문이 모델 로딩을 기다리지 않게
                    catch (Exception e) { Debug.LogWarning("[Alibi] 워밍업 실패: " + e.Message); }
                    hud.SetAi("AI · " + config.Model);
                }
                else hud.SetAi("AI 없음 · 정해진 대사로 진행");
            }
            // 사건 파일을 읽는 동안 AI를 준비했으니, 같은 사건으로 세션만 다시 만든다(아직 아무 행동도 없었다)
            _session = new AlibiSession(_session.Case, _llm, Options());
            _aiReady = true;
            hud.SetStartReady(true);
            RenderAll();
        }

        CharacterArt Art(string name) => characters?.FirstOrDefault(a => a.name == name);
        Sprite CardOf(string name) => Art(name)?.card;
        Sprite FaceOf(string name) => Art(name)?.face;

        Task Play(IEnumerator routine)
        {
            var tcs = new TaskCompletionSource<bool>();
            StartCoroutine(Run(routine, tcs));
            return tcs.Task;
        }

        static IEnumerator Run(IEnumerator routine, TaskCompletionSource<bool> tcs)
        {
            yield return routine;
            tcs.TrySetResult(true);
        }

        // ───────────── 심문 ─────────────

        async void OpenSuspect(string name)
        {
            if (name == null || _busy || _session == null || _session.Over || hud.BriefingOpen) return;
            if (_session.IsDead(name)) { hud.ShowToast($"{Josa.EunNeun(name)} 더 이상 말할 수 없다."); return; }
            _open = name;
            audioFx?.Click();
            int bg = 1 + _session.Case.IndexOf(name) % 2;
            stage.ShowTalk(name, CharacterSheets.Get(name).Tagline, Art(name)?.portrait, bg);
            StartCoroutine(stage.PlayEnter());

            if (!_session.HasMet(name) && _session.CanAct)
            {
                var before = Snapshot();
                _busy = true;
                dialogue.ShowThinking(name, AlibiSession.Opener);
                RenderAll();
                try { await _session.MeetAsync(name, _cts.Token); }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { Debug.LogException(e); }
                _busy = false;
                ShowLast(name, true);
                StartCoroutine(notebook.Pulse(Changed(before)));
                await HandleEvents();
            }
            else ShowLast(name, false);
            RenderAll();
            dialogue.FocusInput();
        }

        void BackToLineup()
        {
            if (_busy) return;
            _open = null;
            stage.ShowLineup();
            dialogue.HideHistory();
            dialogue.ShowNarration(_session.Log.LastOrDefault() ?? "");
            RenderAll();
        }

        void ShowLast(string name, bool animate)
        {
            var chat = _session.Chats[name];
            if (chat.Count == 0) { dialogue.ShowExchange(name, "", "", false); return; }
            string answer = chat[chat.Count - 1].Text;
            string asked = chat.Count >= 2 && chat[chat.Count - 2].Who == AlibiSession.Detective ? chat[chat.Count - 2].Text : "";
            dialogue.ShowExchange(name, asked, answer, animate);
        }

        async void OnQuestion(string q, bool quick)
        {
            if (_busy || _open == null || _session == null || !_session.CanAct) return;
            var before = Snapshot();
            _busy = true;
            dialogue.ShowThinking(_open, q);
            RenderAll();
            try { await _session.AskAsync(_open, q, _cts.Token, quick); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Debug.LogException(e); }
            _busy = false;
            ShowLast(_open, true);
            StartCoroutine(notebook.Pulse(Changed(before)));
            RenderAll();
            await HandleEvents();
            RenderAll();
            dialogue.FocusInput();
        }

        void OnCellClicked(int i, int t)
        {
            if (_busy || _session == null || _session.Over) return;
            var r = new CellRef(_session.Case.Suspects[i].Name, t);
            _focusSlot = t;
            if (!_session.IsKnown(r))
            {
                hud.ShowToast($"{Josa.UiOf(r.Person)} {AlibiCase.TimeLabel(t)} 진술은 아직 모른다 — 직접 물어보자.");
                RenderAll();
                return;
            }
            audioFx?.Click();
            int idx = _selected.FindIndex(x => x.Person == r.Person && x.Slot == r.Slot);
            if (idx >= 0) _selected.RemoveAt(idx);
            else
            {
                _selected.Add(r);
                if (_selected.Count > 2) _selected.RemoveAt(0);
            }
            RenderAll();
        }

        async void OnPresent()
        {
            if (_busy || _session == null || !_session.CanAct || _selected.Count != 2) return;
            var o = _session.Present(_selected[0], _selected[1]);
            if (o == null) return;
            if (o.Valid && !o.Duplicate)
            {
                _busy = true;
                audioFx?.Stamp();
                audioFx?.Duck(true);
                await Play(hud.PlayObjection(o.Message, shakeRoot));
                audioFx?.Duck(false);
                _busy = false;
                _selected.Clear();
                var who = new[] { o.Record.A.Person, o.Record.B.Person }.Distinct().Where(n => !_session.IsDead(n));
                hud.ShowToast($"당사자를 추궁하라 — {string.Join(", ", who)}의 심문 창에 [추궁]이 생겼다.");
            }
            else if (o.Duplicate) hud.ShowToast("이미 찾은 모순이다.");
            else
            {
                StartCoroutine(stage.PlayWrong());
                hud.ShowToast(o.MinutesLost > 0 ? $"<b>모순이 아니다</b> — {o.MinutesLost}분이 흘렀다.\n{o.Message}" : o.Message);
            }
            RenderAll();
            await HandleEvents();
            RenderAll();
        }

        async void OnPress(int recordId)
        {
            if (_busy || _open == null || _session == null) return;
            string who = _open;
            var before = Snapshot();
            _busy = true;
            dialogue.ShowThinking(who, "이 진술, 앞뒤가 안 맞는데. 설명해 봐.");
            RenderAll();
            PressOutcome o = null;
            try { o = await _session.PressAsync(who, recordId, _cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Debug.LogException(e); }
            _busy = false;
            if (o != null)
            {
                ShowLast(who, true);
                var rec = _session.Contradictions.FirstOrDefault(r => r.Id == recordId);
                switch (o.Reaction)
                {
                    case PressReaction.Evade:
                        audioFx?.Stamp();
                        StartCoroutine(stage.PlayEvade());
                        if (rec != null)
                            foreach (var cell in new[] { rec.A, rec.B }.Where(x => x.Person == who))
                                StartCoroutine(notebook.Stamp(_session.Case.IndexOf(who), cell.Slot));
                        hud.ShowToast($"<b>{Josa.EunNeun(who)} 해명하지 못했다.</b>");
                        break;
                    case PressReaction.Confess:
                        audioFx?.Reveal();
                        StartCoroutine(stage.PlayConfess());
                        StartCoroutine(notebook.Pulse(Changed(before)));
                        hud.ShowToast($"<b>{Josa.EunNeun(who)} 비밀을 털어놓았다.</b> 수첩이 파란 잉크로 고쳐졌다.");
                        break;
                    default:
                        StartCoroutine(stage.PlayMaintain());
                        hud.ShowToast($"{Josa.EunNeun(who)} 진술을 굽히지 않는다. 거짓말은 상대 쪽일까?");
                        break;
                }
            }
            RenderAll();
            await HandleEvents();
            RenderAll();
        }

        /// <summary>시계가 흐르다 터진 사건(두 번째 비명·도주)을 컷신으로 보여 준다.</summary>
        async Task HandleEvents()
        {
            while (_session != null && _session.Events.Count > 0)
            {
                var ev = _session.Events.Dequeue();
                if (ev.Kind == SessionEventKind.SecondMurder)
                {
                    _busy = true;
                    audioFx?.Murder();
                    audioFx?.Duck(true);
                    await Play(hud.PlayMurder(ev));
                    audioFx?.Duck(false);
                    _busy = false;
                    if (_open == ev.Victim) { _open = null; stage.ShowLineup(); }
                    _selected.RemoveAll(r => r.Person == ev.Victim);
                    dialogue.ShowNarration(_session.Log.LastOrDefault() ?? "");
                    if (ev.Revealed.HasValue)
                    {
                        int row = _session.Case.IndexOf(ev.Victim);
                        StartCoroutine(notebook.Pulse(Enumerable.Range(0, _session.Case.SlotCount).Select(t => (row, t))));
                    }
                }
                else if (ev.Kind == SessionEventKind.Deadline)
                {
                    _busy = true;
                    await Play(hud.PlayDeadline());
                    _busy = false;
                    OpenAccuse(true);
                }
                RenderAll();
            }
        }

        // ───────────── 지목 ─────────────

        void OpenAccuse(bool forced)
        {
            if (_session == null || _session.Over || (_busy && !forced)) return;
            audioFx?.Click();
            var names = _session.Case.Suspects.Select(p => p.Name).ToList();
            var dead = _session.Case.Suspects.Select(p => p.Dead).ToList();
            hud.ShowAccuse(names, dead, CardOf, forced);
        }

        void Accuse(string name)
        {
            if (_session == null || name == null) return;
            var r = _session.Accuse(name);
            if (r == null) return;
            hud.HideAccuse();
            audioFx?.Ending();
            StartCoroutine(hud.ShowResult(r.Correct, r.Text));
            RenderAll();
        }

        // ───────────── 그리기 ─────────────

        bool[,] Snapshot()
        {
            var c = _session.Case;
            var snap = new bool[c.Suspects.Count, c.SlotCount * 2];
            for (int i = 0; i < c.Suspects.Count; i++)
                for (int t = 0; t < c.SlotCount; t++)
                {
                    var st = _session.Cell(i, t);
                    snap[i, t] = st.LocationKnown;
                    snap[i, c.SlotCount + t] = st.SeenKnown || st.CorrectedFrom != null;
                }
            return snap;
        }

        IEnumerable<(int, int)> Changed(bool[,] before)
        {
            var c = _session.Case;
            var list = new List<(int, int)>();
            for (int i = 0; i < c.Suspects.Count; i++)
                for (int t = 0; t < c.SlotCount; t++)
                {
                    var st = _session.Cell(i, t);
                    if (st.LocationKnown != before[i, t] || (st.SeenKnown || st.CorrectedFrom != null) != before[i, c.SlotCount + t]) list.Add((i, t));
                }
            return list;
        }

        void RenderAll()
        {
            if (_session == null) return;
            hud.SetTop(_session);
            hud.SetDebug(showDebug ? DebugLine() : "");
            bool canAct = _session.CanAct && !_busy;
            notebook.Render(_session, _selected, _focusSlot, _open, FaceOf, canAct && _selected.Count == 2, !_busy && !_session.Over);
            stage.RenderLineup(_session, CardOf);

            if (_open == null)
            {
                dialogue.SetChips(Array.Empty<(string, string)>(), false);
                dialogue.SetPresses(Array.Empty<(int, string, bool)>());
                dialogue.SetInput(false, false);
                return;
            }
            dialogue.SetChips(_session.QuickQuestions(_open), canAct);
            var presses = _session.Contradictions
                .Where(r => r.Involves(_open) && !r.Reactions.ContainsKey(_open))
                .Select(r => (r.Id, r.Label, canAct && _session.CanPress(_open, r)))
                .ToList();
            dialogue.SetPresses(presses);
            dialogue.SetInput(canAct && _session.HasMet(_open), true);
            dialogue.SetHistory(_session.Chats[_open]);
        }

        string DebugLine()
        {
            if (_session.LastIntent == null) return "";
            var s = "해석 " + _session.LastIntent + " · 대사 " + _session.LastSource;
            if (_session.LastProblem != null) s += " · 재생성: " + _session.LastProblem;
            if (_session.LastError != null) s += " · 오류: " + _session.LastError;
            return s;
        }

#if UNITY_EDITOR
        // ── 에디터 스모크 테스트용 창구(실제 클릭과 같은 처리 함수를 부른다) ──
        public AlibiSession EditorSession => _session;
        public bool EditorBusy => _busy;
        public string EditorOpenSuspect => _open;
        public void EditorStart() { hud.HideBriefing(); RenderAll(); }
        public void EditorNewCase(int seed)
        {
            int keep = fixedSeed;
            fixedSeed = seed;
            NewCase();
            fixedSeed = keep;
            _session = new AlibiSession(_session.Case, _llm, Options());
            hud.SetStartReady(_aiReady);
            RenderAll();
        }
        public void EditorOpen(string name) => OpenSuspect(name);
        public void EditorAsk(string question, bool quick) => OnQuestion(question, quick);
        public void EditorSelect(CellRef a, CellRef b) { _selected.Clear(); _selected.Add(a); _selected.Add(b); _focusSlot = b.Slot; RenderAll(); }
        public void EditorPresent() => OnPresent();
        public void EditorPress(int recordId) => OnPress(recordId);
        public void EditorAccuse(string name) => Accuse(name);
#endif
    }
}
