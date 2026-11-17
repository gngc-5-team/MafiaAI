using System;
using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Mystery
{
    /// <summary>대사 한 줄. Speaker가 null이면 서술(탐정의 눈), "탐정"이면 플레이어의 말.</summary>
    public sealed class Line
    {
        public readonly string Speaker;
        public readonly string Text;
        public Line(string speaker, string text) { Speaker = speaker; Text = text; }
        public override string ToString() => Speaker == null ? Text : $"{Speaker}: {Text}";
    }

    public enum Outcome { Ok, Refused, Correct, NearMiss, Wrong }

    public enum Phase { Investigation, Confrontation, Ended }

    /// <summary>행동 하나의 결과 — UI는 이것만 보고 연출한다.</summary>
    public sealed class ActionResult
    {
        public Outcome Outcome = Outcome.Ok;
        public readonly List<Line> Lines = new List<Line>();
        public readonly List<string> Gained = new List<string>();        // 새로 얻은 증거
        public readonly List<string> DetailsFound = new List<string>();
        public readonly List<string> Notices = new List<string>();       // 의문점 열림·풀림 같은 짧은 알림
        public int Minutes;
        public string Message;                                            // 거절·실패 이유
        public bool Breakthrough;                                         // 진술을 깼다 → 「모순!」
        public string Why;                                                // 컷신 아래 설명
        public int WrongBlanks = -1;                                      // 재구성 결과(틀린 빈칸 수)

        public ActionResult Refuse(string why) { Outcome = Outcome.Refused; Message = why; return this; }
    }

    /// <summary>시간이 흘러 터진 일(범인의 움직임) — UI가 컷신·알림으로 보여 준다.</summary>
    public sealed class FiredEvent
    {
        public TimedEvent Event;
        public bool Prevented;
    }

    public sealed class TimelineEntry
    {
        public int At;
        public string Text;
        public string Source;
    }

    /// <summary>
    /// 사건 한 판의 상태. 시간·장소·플래그·얻은 증거만 바꾼다 — 무엇이 참인지는 MysteryCase가 정한다.
    /// 시간을 쓰는 행동: 이동(복도 한 칸 5분)·조사·질문·보여 주기·제시. 읽기·확대·재구성 열람은 공짜.
    /// </summary>
    public sealed class MysterySession
    {
        public readonly MysteryCase Case;
        public int Clock { get; private set; }
        public string Room { get; private set; }
        public Phase Phase { get; private set; } = Phase.Investigation;
        public string EndingId { get; private set; }
        public bool Solved { get; private set; }
        public int TheoryAttempts { get; private set; }
        public int RebuttalIndex { get; private set; }
        public int ResolveLeft { get; private set; }
        public int WrongPresents { get; private set; }
        public int Actions { get; private set; }

        /// <summary>단면판: 그 사람이 같은 방에 있어야 말을 걸 수 있다(평면판은 어디서나 심문).</summary>
        public bool RequirePresence;
        float _walkCarry;
        readonly Dictionary<string, string> _deathRoom = new Dictionary<string, string>();
        readonly Dictionary<string, string> _held = new Dictionary<string, string>();

        readonly HashSet<string> _flags = new HashSet<string>();
        readonly List<string> _found = new List<string>();
        readonly List<TimelineEntry> _timeline = new List<TimelineEntry>();
        readonly HashSet<string> _fired = new HashSet<string>();
        readonly Queue<FiredEvent> _pending = new Queue<FiredEvent>();
        readonly Random _rng;

        public readonly List<Line> Log = new List<Line>();

        public MysterySession(MysteryCase c, int seed = 0)
        {
            Case = c;
            _rng = new Random(seed == 0 ? Environment.TickCount : seed);
            Clock = c.Start;
            Room = c.StartRoom;
            ResolveLeft = c.Resolve;
            _flags.Add("visited:" + Room);
            Refresh(new ActionResult());
        }

        // ───────────────────────── 조회 ─────────────────────────

        public string ClockLabel => MysteryCase.Clock(Clock);
        public int MinutesToDawn => Math.Max(0, Case.Dawn - Clock);
        public Room CurrentRoom => Case.RoomById(Room);
        public IReadOnlyList<string> Found => _found;
        public IEnumerable<Evidence> FoundEvidence => _found.Select(Case.Ev);

        public bool Has(string flag)
        {
            if (flag.StartsWith("at:")) return Room == flag.Substring(3);
            if (flag.StartsWith("held:")) return _held.ContainsKey(flag.Substring(5));
            if (flag == "solved") return Solved;
            return _flags.Contains(flag);
        }

        public bool Eval(string cond) => Cond.Eval(cond, Has);
        public bool HasEvidence(string id) => _found.Contains(id);
        public bool IsDead(string name) => Has("dead:" + name);
        public bool HasMet(string name) => Has("met:" + name);
        public bool Visited(string room) => Has("visited:" + room);

        public IEnumerable<Hotspot> VisibleHotspots(string roomId) =>
            Case.RoomById(roomId).Hotspots.Where(h => Eval(h.ShowIf));

        public bool Examined(Hotspot h) => Has("seen:" + h.Id);

        /// <summary>그 방에서 아직 살피지 않은 곳이 있나(도면에 표시).</summary>
        public int Unexamined(string roomId) => VisibleHotspots(roomId).Count(h => !Examined(h));

        public bool DetailFound(string detailId) => Has("detail:" + detailId);

        public IEnumerable<Statement> Statements(string name)
        {
            var s = Case.SuspectByName(name);
            return s == null ? Enumerable.Empty<Statement>() : s.Statements.Where(st => Has("heard:" + st.Id) && !(st.HideIf != null && Eval(st.HideIf)));
        }

        public bool Broken(Statement st) => Has("broken:" + st.Id);

        public IEnumerable<Topic> Topics(string name)
        {
            var s = Case.SuspectByName(name);
            if (s == null) return Enumerable.Empty<Topic>();
            return s.Topics.Where(t => Eval(t.ShowIf) && !(t.HideIf != null && Eval(t.HideIf)));
        }

        public bool Asked(Topic t) => Has("asked:" + t.Id);
        public bool IsClosed(string name)
        {
            var s = Case.SuspectByName(name);
            return s != null && !string.IsNullOrEmpty(s.ClosedIf) && Eval(s.ClosedIf);
        }

        public IEnumerable<Question> OpenQuestions => Case.Questions.Where(q => Has("qopen:" + q.Id) && !Has("qdone:" + q.Id));
        public IEnumerable<Question> SolvedQuestions => Case.Questions.Where(q => Has("qdone:" + q.Id));

        public IEnumerable<TimelineEntry> Timeline => _timeline.OrderBy(e => e.At);

        public bool WordAvailable(Word w) => w.AlwaysIf != null ? Eval(w.AlwaysIf) : Has("word:" + w.Id);
        public IEnumerable<Word> Words(WordKind kind) => Case.Words.Where(w => w.Kind == kind && WordAvailable(w));

        public int MoveCost(string roomId) => RequirePresence ? Case.TravelMinutes(Room, roomId) : Case.Distance(Room, roomId) * Case.MoveCost;

        // ── 손님의 동선 ──

        Stop CurrentStop(string name)
        {
            Stop best = null;
            foreach (var s in Case.Schedule)
                if (s.Who == name && s.At <= Clock && (best == null || s.At >= best.At)) best = s;
            return best;
        }

        /// <summary>그 사람이 지금 있는 방(숨졌으면 숨진 방). 동선이 없으면 null.</summary>
        public string LocationOf(string name) =>
            _deathRoom.TryGetValue(name, out var room) ? room : _held.TryGetValue(name, out var held) ? held : CurrentStop(name)?.Room;

        /// <summary>마주 앉아 이야기하는 동안은 그 자리를 뜨지 않는다(범인이라면 그동안 아무 짓도 못 한다).</summary>
        public void Hold(string name)
        {
            var at = LocationOf(name);
            if (at != null) _held[name] = at;
        }

        public void Release(string name) { if (name != null) _held.Remove(name); }
        public bool IsHeld(string name) => _held.ContainsKey(name);

        /// <summary>방 안 자리(0~1).</summary>
        public float LocationX(string name) => CurrentStop(name)?.X ?? 0.5f;

        public bool IsHere(string name) => !RequirePresence || LocationOf(name) == Room;

        public IEnumerable<string> PeopleIn(string roomId) =>
            Case.Suspects.Select(s => s.Name).Where(n => !IsDead(n) && LocationOf(n) == roomId);

        /// <summary>UI가 꺼내 보여 줄 사건(컷신·알림)이 남았나.</summary>
        public bool HasPendingEvents => _pending.Count > 0;

        public bool TryTakeEvent(out FiredEvent e)
        {
            if (_pending.Count > 0) { e = _pending.Dequeue(); return true; }
            e = null;
            return false;
        }

        public bool CanAct => Phase == Phase.Investigation;

        // ───────────────────────── 행동 ─────────────────────────

        public ActionResult Move(string roomId)
        {
            var r = new ActionResult();
            if (!CanAct) return r.Refuse("지금은 움직일 수 없다.");
            if (roomId == Room) return r.Refuse("이미 이 방에 있다.");
            int cost = MoveCost(roomId);
            if (cost >= 99 * Case.MoveCost || cost >= 999) return r.Refuse("갈 수 없는 곳이다.");
            Arrive(r, roomId);
            Spend(r, cost);
            Finish(r);
            return r;
        }

        // ── 단면판: 걸어서 움직이기 ──

        /// <summary>걸은 만큼(방 너비 단위) 시간이 흐른다. 1분이 차면 시계가 넘어가고, 그 사이 일어난 일도 터진다.</summary>
        public ActionResult Walk(float roomWidths)
        {
            var r = new ActionResult();
            if (!CanAct || roomWidths <= 0f) return r;
            _walkCarry += roomWidths * Case.WalkMinutesPerRoom;
            if (_walkCarry < 1f) return r;
            int whole = (int)_walkCarry;
            _walkCarry -= whole;
            Spend(r, whole);
            Finish(r);
            return r;
        }

        /// <summary>문을 지나 같은 층 옆방에 들어섰다(시간은 걸음으로 이미 셌다).</summary>
        public ActionResult EnterRoom(string roomId)
        {
            var r = new ActionResult();
            if (roomId == Room) return r;
            if (!Case.Adjacent(Room, roomId) || Case.IsStair(Room, roomId)) return r.Refuse("그쪽으로는 바로 갈 수 없다.");
            Arrive(r, roomId);
            Finish(r);
            return r;
        }

        /// <summary>계단으로 위·아래층에 간다.</summary>
        public ActionResult UseStairs(string toRoom)
        {
            var r = new ActionResult();
            if (!CanAct) return r.Refuse("지금은 움직일 수 없다.");
            if (!Case.Adjacent(Room, toRoom) || !Case.IsStair(Room, toRoom)) return r.Refuse("그 계단으로는 갈 수 없다.");
            Arrive(r, toRoom);
            Spend(r, Case.StairMinutes);
            Finish(r);
            return r;
        }

        void Arrive(ActionResult r, string roomId)
        {
            Room = roomId;
            bool first = !Visited(roomId);
            _flags.Add("visited:" + roomId);
            var room = Case.RoomById(roomId);
            if (first && !string.IsNullOrEmpty(room.Intro)) Say(r, null, room.Intro);
        }

        public ActionResult Examine(string hotspotId)
        {
            var r = new ActionResult();
            var h = VisibleHotspots(Room).FirstOrDefault(x => x.Id == hotspotId);
            if (h == null) return r.Refuse("여기엔 그런 게 없다.");
            bool first = !Examined(h);
            Say(r, null, h.Text);
            if (first && CanAct)
            {
                _flags.Add("seen:" + h.Id);
                foreach (var f in h.SetFlags) _flags.Add(f);
                AddWords(h.Words);
                if (!string.IsNullOrEmpty(h.Gives))
                    foreach (var id in h.Gives.Split(',')) Gain(r, id.Trim());
                Spend(r, string.IsNullOrEmpty(h.Gives) ? Case.FlavorExamineCost : Case.ExamineCost);
            }
            Finish(r);
            return r;
        }

        /// <summary>증거를 확대해 숨은 단서를 찾는다(시간은 들지 않는다 — 꼼꼼히 보는 걸 벌주지 않는다).</summary>
        public ActionResult InspectDetail(string evidenceId, string detailId)
        {
            var r = new ActionResult();
            var ev = Case.Ev(evidenceId);
            if (ev == null || !HasEvidence(evidenceId)) return r.Refuse("아직 가진 증거가 아니다.");
            var d = ev.Details.FirstOrDefault(x => x.Id == detailId);
            if (d == null) return r.Refuse("그런 단서는 없다.");
            Say(r, null, d.Text);
            if (!DetailFound(d.Id))
            {
                _flags.Add("detail:" + d.Id);
                AddWords(d.Words);
                foreach (var n in d.Timeline) AddNote(n, ev.Name);
                r.DetailsFound.Add(d.Id);
            }
            Finish(r);
            return r;
        }

        public ActionResult Meet(string name)
        {
            var r = new ActionResult();
            var s = Case.SuspectByName(name);
            if (s == null) return r.Refuse("그런 손님은 없다.");
            if (IsDead(name)) return r.Refuse($"{name}{Josa(name, "은", "는")} 더 이상 말할 수 없다.");
            if (!IsHere(name)) return r.Refuse(NotHere(name));
            if (!HasMet(name))
            {
                _flags.Add("met:" + name);
                foreach (var g in s.Greeting) Say(r, name, g);
            }
            Finish(r);
            return r;
        }

        public ActionResult Ask(string name, string topicId)
        {
            var r = new ActionResult();
            var s = Case.SuspectByName(name);
            var t = Topics(name).FirstOrDefault(x => x.Id == topicId);
            if (s == null || t == null) return r.Refuse("물을 수 없는 것이다.");
            if (IsDead(name)) return r.Refuse("더 이상 대답할 수 없다.");
            if (!IsHere(name)) return r.Refuse(NotHere(name));
            if (!CanAct) return r.Refuse("지금은 물을 수 없다.");
            Say(r, "탐정", t.Label);
            if (IsClosed(name)) { Say(r, name, s.ClosedLine); Finish(r); return r; }
            foreach (var l in t.Lines) Say(r, name, l);
            if (!Asked(t))
            {
                _flags.Add("asked:" + t.Id);
                foreach (var f in t.SetFlags) _flags.Add(f);
                AddWords(t.Words);
                foreach (var g in t.Gives) Gain(r, g);
                Spend(r, Case.AskCost);
            }
            Finish(r);
            return r;
        }

        /// <summary>진술과 상관없이 증거를 보여 주고 반응을 본다(누구 물건인지 같은 실마리).</summary>
        public ActionResult Show(string name, string evidenceId)
        {
            var r = new ActionResult();
            var s = Case.SuspectByName(name);
            var ev = Case.Ev(evidenceId);
            if (s == null || ev == null || !HasEvidence(evidenceId)) return r.Refuse("보여 줄 수 없다.");
            if (IsDead(name)) return r.Refuse("더 이상 대답할 수 없다.");
            if (!IsHere(name)) return r.Refuse(NotHere(name));
            if (!CanAct) return r.Refuse("지금은 보여 줄 수 없다.");
            Say(r, "탐정", $"이걸 보시죠 — {ev.Name}.");
            string key = "shown:" + name + ":" + evidenceId;
            string line = IsClosed(name) ? s.ClosedLine : s.Reactions.TryGetValue(evidenceId, out var re) ? re : s.DefaultReaction;
            Say(r, name, line);
            if (!Has(key))
            {
                _flags.Add(key);
                Spend(r, Case.ShowCost);
            }
            Finish(r);
            return r;
        }

        /// <summary>진술 한 줄에 증거를 들이민다. 맞으면 진술이 깨지고(비밀·이야기가 열림), 틀리면 시간이 크게 흐른다.</summary>
        public ActionResult Present(string name, string statementId, string evidenceId)
        {
            var r = new ActionResult();
            var s = Case.SuspectByName(name);
            var st = Statements(name).FirstOrDefault(x => x.Id == statementId);
            var ev = Case.Ev(evidenceId);
            if (s == null || st == null || ev == null || !HasEvidence(evidenceId)) return r.Refuse("제시할 수 없다.");
            if (IsDead(name)) return r.Refuse("더 이상 대답할 수 없다.");
            if (!IsHere(name)) return r.Refuse(NotHere(name));
            if (!CanAct) return r.Refuse("지금은 제시할 수 없다.");
            if (Broken(st)) return r.Refuse("이미 무너진 진술이다.");
            Say(r, "탐정", $"「{st.Text}」 — 그렇다면 이건 어떻게 설명하시겠습니까? {ev.Name}.");

            if (Accepts(st.BrokenBy, evidenceId))
            {
                r.Outcome = Outcome.Correct;
                r.Breakthrough = true;
                r.Why = st.Why;
                _flags.Add("broken:" + st.Id);
                foreach (var f in st.SetFlags) _flags.Add(f);
                AddWords(st.Words);
                foreach (var l in st.BreakLines) Say(r, name, l);
                foreach (var g in st.Gives) Gain(r, g);
                Spend(r, Case.PresentCost);
            }
            else if (st.BrokenBy.Any(p => ProofEvidence(p) == evidenceId) || st.Related.Contains(evidenceId))
            {
                r.Outcome = Outcome.NearMiss;
                Say(r, name, st.NearMiss ?? Pick(s.WrongLines));
                bool needsDetail = st.BrokenBy.Any(p => ProofEvidence(p) == evidenceId && ProofFlag(p) != null);
                r.Message = needsDetail ? "방향은 맞다. 이 증거를 더 자세히 들여다보면 무언가 보일지도 모른다."
                                        : "방향은 맞다. 하지만 이것만으로는 결정적이지 않다.";
                Spend(r, Case.NearMissCost);
            }
            else
            {
                r.Outcome = Outcome.Wrong;
                WrongPresents++;
                Say(r, name, Pick(s.WrongLines));
                r.Message = $"헛짚었다. {Case.WrongCost}분이 허비됐다.";
                Spend(r, Case.WrongCost);
            }
            Finish(r);
            return r;
        }

        /// <summary>자유 질문(LLM)에 쓴 시간을 기록한다 — 답 자체는 MysteryTalk가 만든다.</summary>
        public ActionResult RecordFreeQuestion(string name, string question, string answer)
        {
            var r = new ActionResult();
            if (!CanAct || IsDead(name)) return r.Refuse("지금은 물을 수 없다.");
            if (!IsHere(name)) return r.Refuse(NotHere(name));
            Say(r, "탐정", question);
            Say(r, name, answer);
            Spend(r, Case.FreeAskCost);
            Finish(r);
            return r;
        }

        // ───────────────────────── 재구성 · 최후의 추궁 ─────────────────────────

        /// <summary>빈칸을 모두 채운 추리를 낸다. 전부 맞으면 최후의 추궁으로, 틀리면 30분이 흐른다.</summary>
        public ActionResult SubmitTheory(IDictionary<string, string> answers)
        {
            var r = new ActionResult();
            if (!CanAct) return r.Refuse("지금은 추리를 낼 수 없다.");
            if (Case.Blanks.Any(b => !answers.TryGetValue(b.Id, out var v) || string.IsNullOrEmpty(v)))
                return r.Refuse("빈칸을 모두 채워야 한다.");
            TheoryAttempts++;
            int wrong = Case.Blanks.Count(b => answers[b.Id] != b.Answer);
            r.WrongBlanks = wrong;
            if (wrong == 0)
            {
                r.Outcome = Outcome.Correct;
                _flags.Add("theory_ok");
                Phase = Phase.Confrontation;
                RebuttalIndex = 0;
                ResolveLeft = Case.Resolve;
                foreach (var l in Case.Rehearsal) SayScripted(r, l);
                if (CurrentRebuttal != null) Say(r, Case.Killer, CurrentRebuttal.Line);
            }
            else
            {
                r.Outcome = Outcome.Wrong;
                r.Message = wrong <= 2 ? "거의 맞았다 — 틀린 빈칸은 두 개 이하다." : "추리가 맞지 않는다. 다시 생각해 보자.";
                Spend(r, Case.WrongTheoryCost);
            }
            Finish(r);
            return r;
        }

        public Rebuttal CurrentRebuttal => Phase == Phase.Confrontation && RebuttalIndex < Case.Rebuttals.Length ? Case.Rebuttals[RebuttalIndex] : null;

        /// <summary>최후의 추궁: 범인의 반박에 증거로 맞선다.</summary>
        public ActionResult Rebut(string evidenceId)
        {
            var r = new ActionResult();
            var reb = CurrentRebuttal;
            var ev = Case.Ev(evidenceId);
            if (reb == null || ev == null || !HasEvidence(evidenceId)) return r.Refuse("지금은 제시할 수 없다.");
            Say(r, "탐정", $"{reb.Counter ?? "이걸 보시죠."} — {ev.Name}.");
            if (Accepts(reb.Accept, evidenceId))
            {
                r.Outcome = Outcome.Correct;
                r.Breakthrough = true;
                foreach (var l in reb.Success) SayScripted(r, l);
                RebuttalIndex++;
                if (RebuttalIndex >= Case.Rebuttals.Length)
                {
                    foreach (var l in Case.Confession) SayScripted(r, l);
                    End(true);
                }
                else Say(r, Case.Killer, CurrentRebuttal.Line);
            }
            else
            {
                r.Outcome = Outcome.Wrong;
                ResolveLeft--;
                Say(r, Case.Killer, reb.Wrong);
                Spend(r, Case.WrongRebuttalCost, allowDuringConfrontation: true);
                if (ResolveLeft <= 0)
                {
                    Phase = Phase.Investigation;
                    RebuttalIndex = 0;
                    ResolveLeft = Case.Resolve;
                    Say(r, Case.Killer, "증거가 부족하군요, 탐정님. 이야기는 여기까지입니다.");
                    r.Message = "추궁이 막혔다. 증거를 더 모아 다시 몰아붙이자. (추리는 그대로 남아 있다)";
                }
            }
            Finish(r);
            return r;
        }

        /// <summary>추리가 맞은 뒤 수사로 물러났다가 다시 추궁을 시작한다.</summary>
        public ActionResult ResumeConfrontation()
        {
            var r = new ActionResult();
            if (!Has("theory_ok") || Phase != Phase.Investigation) return r.Refuse("아직 추리가 완성되지 않았다.");
            Phase = Phase.Confrontation;
            RebuttalIndex = 0;
            ResolveLeft = Case.Resolve;
            Say(r, Case.Killer, CurrentRebuttal.Line);
            return r;
        }

        // ───────────────────────── 내부 ─────────────────────────

        static string ProofEvidence(string proof) { int i = proof.IndexOf('@'); return i < 0 ? proof : proof.Substring(0, i); }
        static string ProofFlag(string proof) { int i = proof.IndexOf('@'); return i < 0 ? null : proof.Substring(i + 1); }

        bool Accepts(IEnumerable<string> proofs, string evidenceId) =>
            proofs.Any(p => ProofEvidence(p) == evidenceId && (ProofFlag(p) == null || Has(ProofFlag(p))));

        void Gain(ActionResult r, string id)
        {
            if (string.IsNullOrEmpty(id) || _found.Contains(id)) return;
            var ev = Case.Ev(id);
            if (ev == null) return;
            _found.Add(id);
            _flags.Add("has:" + id);
            AddWords(ev.Words);
            foreach (var n in ev.Timeline) AddNote(n, ev.Kind == EvidenceKind.Testimony ? ev.Source : ev.Name);
            r.Gained.Add(id);
        }

        void AddWords(IEnumerable<string> words)
        {
            if (words == null) return;
            foreach (var w in words) _flags.Add("word:" + w);
        }

        void AddNote(TimeNote n, string source)
        {
            if (_timeline.Any(e => e.At == n.At && e.Text == n.Text)) return;
            _timeline.Add(new TimelineEntry { At = n.At, Text = n.Text, Source = source });
        }

        void Say(ActionResult r, string speaker, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var l = new Line(speaker, text);
            r.Lines.Add(l);
            Log.Add(l);
        }

        /// <summary>"[조건]인물|대사" 형식(조건은 생략 가능).</summary>
        void SayScripted(ActionResult r, string raw)
        {
            string s = raw;
            if (s.StartsWith("["))
            {
                int close = s.IndexOf(']');
                if (close > 0)
                {
                    if (!Eval(s.Substring(1, close - 1))) return;
                    s = s.Substring(close + 1);
                }
            }
            int bar = s.IndexOf('|');
            if (bar < 0) Say(r, null, s);
            else Say(r, s.Substring(0, bar), s.Substring(bar + 1));
        }

        void Spend(ActionResult r, int minutes, bool allowDuringConfrontation = false)
        {
            if (minutes <= 0 || Phase == Phase.Ended) return;
            if (Phase == Phase.Confrontation && !allowDuringConfrontation) return;
            Clock += minutes;
            r.Minutes += minutes;
            Actions++;
            foreach (var e in Case.Events.OrderBy(x => x.At))
            {
                if (_fired.Contains(e.Id) || Clock < e.At) continue;
                _fired.Add(e.Id);
                if (!string.IsNullOrEmpty(e.Unless) && Eval(e.Unless))
                {
                    if (!string.IsNullOrEmpty(e.PreventedToast)) _pending.Enqueue(new FiredEvent { Event = e, Prevented = true });
                    continue;
                }
                foreach (var f in e.SetFlags) _flags.Add(f);
                if (!string.IsNullOrEmpty(e.Kill))
                {
                    _flags.Add("dead:" + e.Kill);
                    _deathRoom[e.Kill] = e.Room ?? LocationOf(e.Kill);
                }
                _pending.Enqueue(new FiredEvent { Event = e });
                Log.Add(new Line(null, $"[{MysteryCase.Clock(e.At)}] {e.CutSub ?? e.Toast}"));
            }
            if (Clock >= Case.Dawn && Phase != Phase.Ended) End(false);
        }

        void End(bool solved)
        {
            Solved = solved;
            Phase = Phase.Ended;
            if (solved) _flags.Add("solved");
            var ending = Case.Endings.FirstOrDefault(e => e.Solved == solved && (e.Id != "two_graves" || IsDead("세이")) && (e.Id != "witness" || !IsDead("세이")))
                         ?? Case.Endings.FirstOrDefault(e => e.Solved == solved);
            EndingId = ending?.Id;
        }

        void Finish(ActionResult r) => Refresh(r);

        /// <summary>행동이 끝날 때마다: 새로 들린 진술 → 증언 기록, 의문점 열림·풀림.</summary>
        void Refresh(ActionResult r)
        {
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 10)
            {
                changed = false;
                foreach (var s in Case.Suspects)
                {
                    if (!HasMet(s.Name)) continue;
                    foreach (var st in s.Statements)
                    {
                        if (Has("heard:" + st.Id) || !Eval(st.ShowIf)) continue;
                        _flags.Add("heard:" + st.Id);
                        changed = true;
                        if (!string.IsNullOrEmpty(st.Hears)) Gain(r, st.Hears);
                    }
                }
            }
            foreach (var q in Case.Questions)
            {
                if (!Has("qopen:" + q.Id) && Eval(q.OpenIf))
                {
                    _flags.Add("qopen:" + q.Id);
                    r.Notices.Add("의문점 — " + q.Text);
                }
                if (Has("qopen:" + q.Id) && !Has("qdone:" + q.Id) && !string.IsNullOrEmpty(q.ResolveIf) && Eval(q.ResolveIf))
                {
                    _flags.Add("qdone:" + q.Id);
                    r.Notices.Add("풀렸다 — " + q.Answer);
                }
            }
        }

        string Pick(string[] lines) => lines == null || lines.Length == 0 ? "……" : lines[_rng.Next(lines.Length)];

        string NotHere(string name)
        {
            var at = LocationOf(name);
            return $"{name}{Josa(name, "은", "는")} 여기 없다" + (at != null ? $" — 지금 {Case.FloorLabel(at)} {Case.RoomById(at).Name}에 있다." : ".");
        }

        static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return without;
            return (c - 0xAC00) % 28 != 0 ? withBatchim : without;
        }

        // ───────────────────────── 재구성 문장 ─────────────────────────

        /// <summary>"{빈칸:조사}" 자리를 고른 단어(또는 빈칸 표시)로 채운다. 조사: 이(가) 은(는) 을(를) 로(으로) 와(과).</summary>
        public string RenderSentence(string template, IDictionary<string, string> answers, Func<string, string, string> decorate = null)
        {
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0) { sb.Append(template.Substring(i)); break; }
                sb.Append(template.Substring(i, open - i));
                int close = template.IndexOf('}', open);
                string token = template.Substring(open + 1, close - open - 1);
                string id = token, josa = null;
                int colon = token.IndexOf(':');
                if (colon >= 0) { id = token.Substring(0, colon); josa = token.Substring(colon + 1); }
                string word = answers != null && answers.TryGetValue(id, out var wid) && wid != null ? Case.WordById(wid)?.Label : null;
                string shown = word ?? "＿＿＿＿";
                string particle = josa == null ? "" : word == null ? Generic(josa) : Particle(word, josa);
                sb.Append(decorate != null ? decorate(id, shown) : shown);
                sb.Append(particle);
                i = close + 1;
            }
            return sb.ToString();
        }

        static string Generic(string josa) => josa switch
        {
            "이" or "가" => "이(가)",
            "은" or "는" => "은(는)",
            "을" or "를" => "을(를)",
            "와" or "과" => "와(과)",
            _ => "(으)로",
        };

        static string Particle(string word, string josa)
        {
            char c = word[word.Length - 1];
            bool hangul = c >= 0xAC00 && c <= 0xD7A3;
            int jong = hangul ? (c - 0xAC00) % 28 : 0;
            bool batchim = hangul && jong != 0;
            switch (josa)
            {
                case "이": case "가": return batchim ? "이" : "가";
                case "은": case "는": return batchim ? "은" : "는";
                case "을": case "를": return batchim ? "을" : "를";
                case "와": case "과": return batchim ? "과" : "와";
                default: return batchim && jong != 8 ? "으로" : "로";
            }
        }
    }
}
