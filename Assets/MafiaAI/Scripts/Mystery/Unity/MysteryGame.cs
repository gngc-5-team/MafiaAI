using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using MafiaAI.Alibi;
using MafiaAI.Alibi.UI;
using MafiaAI.LLM;

namespace MafiaAI.Mystery.UI
{
    /// <summary>
    /// 「자정의 원고」 진행자. 엔진(MysterySession)과 씬 저작 뷰를 잇는다.
    /// 진실·판정은 사건 데이터가, 자유 질문의 말투는 LLM이, 화면은 뷰가 맡는다 — 여기서는 클릭을 엔진 호출로 바꾸고 결과를 연출할 뿐이다.
    /// </summary>
    public class MysteryGame : MonoBehaviour
    {
        [Header("AI")]
        [SerializeField] GameConfig config = new GameConfig();
        [Tooltip("끄면 직접 묻기는 정해진 얼버무림으로만 답한다(사건 진행에는 영향 없음)")]
        [SerializeField] bool useLlm = true;

        [Header("진행")]
        [Tooltip("프롤로그를 건너뛴다(반복 테스트용)")]
        [SerializeField] bool skipPrologue = false;

        [Header("뷰(씬 저작)")]
        public MysteryHud hud;
        public RoomView room;
        public TalkView talk;
        public CaseBookView book;
        public EvidenceViewer viewer;
        public ReaderView reader;
        public TheoryView theory;
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
            public Sprite card;
            public Sprite portrait;
            public Sprite face;
        }

        public SpriteEntry[] art;
        public CharacterArt[] characters;

        MysterySession _session;
        ILlm _llm;
        string _open;                // 마주 앉은 사람
        string _statement;           // 고른 진술
        bool _busy;
        bool _clicked;
        bool _autoAdvance;           // 자동 검증: 대사를 클릭 없이 넘긴다
        Dictionary<string, Sprite> _art;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // ───────────────────────── 시작 ─────────────────────────

        void Awake()
        {
            _art = new Dictionary<string, Sprite>();
            if (art != null) foreach (var e in art) if (e != null && !string.IsNullOrEmpty(e.key)) _art[e.key] = e.sprite;

            room.HotspotClicked += id => Run(Examine(id));
            room.MoveClicked += id => Run(Move(id));
            room.InterrogateClicked += () => { if (!_busy) ShowLineup(); };
            room.DescriptionClicked += () => { if (room.Typing) room.CompleteTyping(); };
            talk.CardClicked += i => { if (!_busy) Run(OpenSuspect(_session.Case.Suspects[i].Name)); };
            talk.LineupBackClicked += () => { if (!_busy) ShowRoom(); };
            talk.BackClicked += () => { if (!_busy && _session.Phase != Phase.Confrontation) ShowLineup(); };
            talk.StatementClicked += SelectStatement;
            talk.ChipClicked += id => Run(Ask(id));
            talk.QuestionSubmitted += q => Run(FreeAsk(q));
            talk.BoxClicked += () => { if (talk.Typing) talk.CompleteTyping(); else _clicked = true; };
            book.ActClicked += id => Run(Act(id));
            book.InspectClicked += Inspect;
            book.ReadClicked += Read;
            book.ChapterClicked += ReadChapter;
            book.TheoryClicked += OpenTheory;
            viewer.DetailClicked += (ev, d) => Run(FindDetail(ev, d));
            theory.Submitted += () => Run(SubmitTheory());
            hud.RestartClicked += Restart;
        }

        void Start()
        {
            _session = new MysterySession(Case01.Build());
            _ = PrepareAiAsync();
            Run(Opening());
        }

        void OnDestroy() => _cts.Cancel();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame) Escape();
            if ((kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) && _busy && !TypingInInput()) _clicked = true;
        }

        bool TypingInInput() => talk.input != null && talk.input.isFocused;

        void Escape()
        {
            if (theory.Open) { if (theory.pickerRoot != null && theory.pickerRoot.activeSelf) theory.pickerRoot.SetActive(false); else theory.Close(); return; }
            if (viewer.Open) { viewer.Close(); return; }
            if (reader.Open) { reader.Close(); return; }
            if (_busy || _session == null) return;
            if (talk.InTalk && _session.Phase != Phase.Confrontation) { ShowLineup(); return; }
            if (talk.Visible && !talk.InTalk) ShowRoom();
        }

        async Task PrepareAiAsync()
        {
            if (!useLlm) { hud.SetAi("AI 꺼짐 · 직접 묻기는 짧게 얼버무림"); return; }
            hud.SetAi("AI 확인 중…");
            bool ok = false;
            try { ok = await OllamaBootstrap.EnsureReadyAsync(config); }
            catch (Exception e) { Debug.LogWarning("[Mystery] AI 준비 실패: " + e.Message); }
            if (!ok) { hud.SetAi("AI 없음 · 직접 묻기는 짧게 얼버무림"); return; }
            _llm = new OllamaLlm(config);
            hud.SetAi("AI 깨우는 중…");
            try { await _llm.GenerateAsync("", "준비", 0f, false, 1, _cts.Token); }
            catch (Exception e) { Debug.LogWarning("[Mystery] 워밍업 실패: " + e.Message); }
            hud.SetAi("AI · " + config.Model);
        }

        IEnumerator Opening()
        {
            ShowRoom(describe: false);
            RenderAll();
            if (!skipPrologue) yield return hud.PlayStory(_session.Case.Prologue, Art, "자정의 원고");
            room.Describe(_session.CurrentRoom.Name, _session.CurrentRoom.Intro + "\n<size=85%><color=#8A6D4E>물건에 마우스를 대면 붉은 동그라미가 뜬다. 눌러서 살펴보자.</color></size>");
            hud.Toast("수사 시작 — 날이 밝으면 경찰이 온다. 그 전에 진실을 밝혀라.");
        }

        void Restart()
        {
            hud.HideReport();
            _session = new MysterySession(Case01.Build());
            _open = null;
            _statement = null;
            Run(Opening());
        }

        // ───────────────────────── 화면 전환 ─────────────────────────

        void ShowRoom(bool describe = true)
        {
            _open = null;
            _statement = null;
            talk.SetVisible(false);
            room.SetVisible(true);
            room.ShowRoom(_session, Art);
            room.RenderMap(_session);
            book.SetMode(CaseBookView.ActMode.None);
            if (describe) room.Describe(_session.CurrentRoom.Name, _session.CurrentRoom.Intro, false);
            RenderAll();
        }

        void ShowLineup()
        {
            _open = null;
            _statement = null;
            room.SetVisible(false);
            talk.ShowLineup(_session, n => Character(n)?.card);
            talk.SetConfront(false);
            book.SetMode(CaseBookView.ActMode.None);
            RenderAll();
        }

        IEnumerator OpenSuspect(string name)
        {
            if (_session.IsDead(name)) { hud.Toast(name + Particle(name, "은", "는") + " 더 이상 말할 수 없다."); yield break; }
            audioFx?.Click();
            _open = name;
            _statement = null;
            var who = _session.Case.SuspectByName(name);
            room.SetVisible(false);
            talk.ShowTalk(who, Character(name)?.portrait);
            talk.SetConfront(false);
            RenderTalk();
            book.SetMode(CaseBookView.ActMode.Show);
            StartCoroutine(talk.PlayEnter());
            var r = _session.Meet(name);
            if (r.Lines.Count > 0) yield return PlayLines(r.Lines, null);
            else talk.Say(name, who.Bio);
            yield return After(r);
        }

        void SelectStatement(string id)
        {
            if (_busy || _open == null) return;
            audioFx?.Click();
            _statement = _statement == id ? null : id;
            book.SetMode(_statement != null ? CaseBookView.ActMode.Present : CaseBookView.ActMode.Show);
            if (_statement != null) book.SetTab(0);
            RenderTalk();
        }

        // ───────────────────────── 행동 ─────────────────────────

        IEnumerator Examine(string hotspotId)
        {
            audioFx?.Click();
            var r = _session.Examine(hotspotId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            var h = _session.CurrentRoom.Hotspots.First(x => x.Id == hotspotId);
            room.Describe(h.Label, string.Join("\n", r.Lines.Select(l => l.Text)));
            room.RefreshPins();
            if (r.Gained.Count > 0) audioFx?.Stamp();
            yield return After(r);
        }

        IEnumerator Move(string roomId)
        {
            if (roomId == _session.Room) yield break;
            var r = _session.Move(roomId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            audioFx?.Click();
            room.ShowRoom(_session, Art);
            room.Describe(_session.CurrentRoom.Name, _session.CurrentRoom.Intro);
            yield return After(r);
        }

        IEnumerator Ask(string topicId)
        {
            if (_open == null) yield break;
            audioFx?.Click();
            var r = _session.Ask(_open, topicId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            yield return PlayLines(r.Lines.Where(l => l.Speaker != "탐정").ToList(), r.Lines.FirstOrDefault(l => l.Speaker == "탐정")?.Text);
            yield return After(r);
        }

        IEnumerator FreeAsk(string question)
        {
            if (_open == null || _session.IsDead(_open)) yield break;
            string name = _open;
            if (_session.IsClosed(name))
            {
                talk.Say(name, _session.Case.SuspectByName(name).ClosedLine, "탐정 — " + question);
                yield break;
            }
            talk.SetInputEnabled(false);
            talk.Thinking(name, "탐정 — " + question);
            var task = MysteryTalk.AnswerAsync(useLlm ? _llm : null, _session, name, question, _cts.Token);
            while (!task.IsCompleted) yield return null;
            string answer = task.Status == TaskStatus.RanToCompletion ? task.Result : MysteryTalk.Deflect(name);
            var r = _session.RecordFreeQuestion(name, question, answer);
            talk.Say(name, answer, "탐정 — " + question);
            talk.SetInputEnabled(true);
            yield return After(r);
        }

        IEnumerator Act(string evidenceId)
        {
            if (_session.Phase == Phase.Confrontation) { yield return Rebut(evidenceId); yield break; }
            if (_open == null) yield break;
            if (_statement == null)
            {
                audioFx?.Click();
                var r = _session.Show(_open, evidenceId);
                if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
                yield return PlayLines(r.Lines.Where(l => l.Speaker != "탐정").ToList(), "탐정 — " + _session.Case.Ev(evidenceId).Name + "을(를) 내민다");
                yield return After(r);
                yield break;
            }

            var res = _session.Present(_open, _statement, evidenceId);
            if (res.Outcome == Outcome.Refused) { hud.Toast(res.Message); yield break; }
            string asked = "탐정 — " + _session.Case.Ev(evidenceId).Name + "을(를) 들이민다";
            var said = res.Lines.Where(l => l.Speaker != "탐정").ToList();
            if (res.Breakthrough)
            {
                audioFx?.Stamp();
                audioFx?.Duck(true);
                yield return hud.PlayObjection("모순!", res.Why, shakeRoot);
                audioFx?.Duck(false);
                StartCoroutine(talk.PlayBreak());
                _statement = null;
                book.SetMode(CaseBookView.ActMode.Show);
                RenderTalk();
                yield return PlayLines(said, asked);
                if (res.Gained.Count > 0) audioFx?.Reveal();
            }
            else
            {
                StartCoroutine(talk.PlayWrong());
                yield return PlayLines(said, asked);
                if (!string.IsNullOrEmpty(res.Message)) hud.Toast(res.Message);
            }
            yield return After(res);
        }

        IEnumerator Rebut(string evidenceId)
        {
            var r = _session.Rebut(evidenceId);
            if (r.Outcome == Outcome.Refused) { hud.Toast(r.Message); yield break; }
            if (r.Breakthrough)
            {
                audioFx?.Stamp();
                audioFx?.Duck(true);
                yield return hud.PlayObjection(_session.Phase == Phase.Ended ? "자백" : "반박!", null, shakeRoot);
                audioFx?.Duck(false);
                StartCoroutine(talk.PlayBreak());
            }
            else StartCoroutine(talk.PlayWrong());
            talk.SetConfront(_session.Phase == Phase.Confrontation, _session.ResolveLeft);
            yield return PlayLines(r.Lines, null);
            if (!string.IsNullOrEmpty(r.Message)) hud.Toast(r.Message);
            if (_session.Phase == Phase.Investigation) { talk.SetConfront(false); book.SetMode(CaseBookView.ActMode.Show); }
            yield return After(r);
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
                StartCoroutine(talk.PlayWrong());
                theory.SetFeedback($"{r.Message}  <color=#9E1B30>({_session.Case.WrongTheoryCost}분이 흘렀다)</color>");
                theory.Show(_session);
                yield return After(r);
                yield break;
            }
            theory.Close();
            audioFx?.Reveal();
            yield return BeginConfrontation(r);
        }

        IEnumerator BeginConfrontation(ActionResult r)
        {
            string killer = _session.Case.Killer;
            _open = killer;
            _statement = null;
            room.SetVisible(false);
            talk.ShowTalk(_session.Case.SuspectByName(killer), Character(killer)?.portrait);
            talk.SetConfront(true, _session.ResolveLeft);
            RenderTalk();
            book.SetMode(CaseBookView.ActMode.Rebut);
            book.SetTab(0);
            StartCoroutine(talk.PlayEnter());
            yield return PlayLines(r.Lines, null);
            yield return After(r);
        }

        // ───────────────────────── 결과 처리 ─────────────────────────

        IEnumerator After(ActionResult r)
        {
            if (r.Gained.Count > 0)
            {
                var first = _session.Case.Ev(r.Gained[0]);
                string head = first.Kind == EvidenceKind.Testimony ? "증언 기록 — " : first.Kind == EvidenceKind.Document ? "수첩에 끼움 — " : "증거 확보 — ";
                hud.Toast(head + first.Name + (r.Gained.Count > 1 ? $"  외 {r.Gained.Count - 1}건" : ""));
            }
            if (r.Gained.Count > 0) book.Select(r.Gained.Last());
            foreach (var n in r.Notices) hud.Toast(n);
            RenderAll();
            yield return Events();
            if (_session.Phase == Phase.Ended) yield return Ending();
        }

        IEnumerator Events()
        {
            while (_session.TryTakeEvent(out var ev))
            {
                var e = ev.Event;
                if (ev.Prevented) { hud.Toast(e.PreventedToast); continue; }
                if (!string.IsNullOrEmpty(e.CutTitle))
                {
                    audioFx?.Murder();
                    audioFx?.Duck(true);
                    yield return hud.PlayMurder(e.CutTitle, e.CutSub, e.CutNote);
                    audioFx?.Duck(false);
                    if (_open != null && _session.IsDead(_open)) ShowLineup();
                    if (room.Visible && _session.Room == e.Room) room.ShowRoom(_session, Art);
                }
                else hud.Toast(e.Toast);
            }
            RenderAll();
        }

        IEnumerator Ending()
        {
            var c = _session.Case;
            var ending = c.Endings.FirstOrDefault(e => e.Id == _session.EndingId);
            yield return new WaitForSecondsRealtime(0.6f);
            audioFx?.Ending();
            if (ending != null) yield return hud.PlayStory(ending.Panels, Art, ending.Title);
            yield return hud.ShowReport(_session.Solved, (_session.Solved ? "사건 보고서 — " : "사건 보고서 — 미제 · ") + (ending?.Title ?? ""), Report());
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

        /// <summary>대사를 한 줄씩 흘린다. 클릭(또는 스페이스)으로 넘긴다. 마지막 줄은 기다리지 않는다.</summary>
        IEnumerator PlayLines(IList<Line> lines, string asked)
        {
            if (lines == null || lines.Count == 0) yield break;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                talk.Say(l.Speaker ?? "", l.Text, i == 0 ? asked : null);
                if (i == lines.Count - 1) break;
                _clicked = false;
                float t = 0f;
                while (true)
                {
                    t += Time.unscaledDeltaTime;
                    if (_autoAdvance && !talk.Typing && t > 0.3f) break;
                    if (_clicked && !talk.Typing) break;
                    if (_clicked && talk.Typing) { talk.CompleteTyping(); _clicked = false; }
                    yield return null;
                }
            }
        }

        // ───────────────────────── 그리기 ─────────────────────────

        void RenderAll()
        {
            if (_session == null) return;
            hud.SetTop(_session);
            book.Render(_session, Art);
            if (room.Visible) { room.RenderMap(_session); room.RefreshPins(); }
            if (talk.Visible && !talk.InTalk) talk.ShowLineup(_session, n => Character(n)?.card);
            if (talk.InTalk) RenderTalk();
        }

        void RenderTalk()
        {
            if (_open == null) return;
            talk.RenderStatements(_session, _open, _statement);
            bool confront = _session.Phase == Phase.Confrontation;
            var chips = confront ? Enumerable.Empty<(string, string, bool)>()
                : _session.Topics(_open).Select(t => (t.Id, t.Label, _session.Asked(t)));
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
            room.SetInteractable(false);
            yield return routine;
            _busy = false;
            room.SetInteractable(_session.CanAct);
            if (talk.InTalk) talk.SetInputEnabled(_session.Phase == Phase.Investigation);
        }

        Sprite Art(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key.StartsWith("face:")) return Character(key.Substring(5))?.face;
            return _art != null && _art.TryGetValue(key, out var s) ? s : null;
        }

        CharacterArt Character(string name) => characters?.FirstOrDefault(c => c.name == name);

        static string Particle(string w, string with, string without)
        {
            char c = w[w.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0 ? with : without;
        }

        // ───────────────────────── 자동 검증용 ─────────────────────────

        public MysterySession EditorSession => _session;
        public bool EditorBusy => _busy;
        public void EditorAutoAdvance(bool on) => _autoAdvance = on;
        public void EditorClick() => _clicked = true;
        public void EditorExamine(string id) => Run(Examine(id));
        public void EditorMove(string roomId) => Run(Move(roomId));
        public void EditorLineup() { if (!_busy) ShowLineup(); }
        public void EditorRoom() { if (!_busy) ShowRoom(); }
        public void EditorOpen(string name) => Run(OpenSuspect(name));
        public void EditorAsk(string topicId) => Run(Ask(topicId));
        public void EditorFreeAsk(string q) => Run(FreeAsk(q));
        public void EditorSelectStatement(string id) { _statement = null; SelectStatement(id); }
        public void EditorAct(string evidenceId) => Run(Act(evidenceId));
        public void EditorInspect(string evidenceId) => Inspect(evidenceId);
        public void EditorDetail(string evidenceId, string detailId) => Run(FindDetail(evidenceId, detailId));
        public void EditorRead(string evidenceId) => Read(evidenceId);
        public void EditorCloseOverlays() { if (viewer.Open) viewer.Close(); if (reader.Open) reader.Close(); if (theory.Open) theory.Close(); }
        public void EditorTheory(IDictionary<string, string> answers) { theory.Show(_session); theory.Fill(answers); }
        public void EditorSubmitTheory() => Run(SubmitTheory());
        public void EditorSelectEvidence(string id) { book.SetTab(0); book.Select(id); }
    }
}
