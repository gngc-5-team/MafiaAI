using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MafiaAI.Alibi
{
    public sealed class SessionOptions
    {
        public bool UseLlm = true;              // false = 규칙 해석 + 템플릿 대사('AI 없이도 재밌나' 비교용)
        public float VoiceTemperature = 0.55f;  // 재현성 우선(2026-08-01 교훈: 1.0+ 온도는 교차 검증을 무의미하게 만든다)
        public float IntentTemperature = 0.1f;
        public int VoiceMaxTokens = 120;
        public int IntentMaxTokens = 120;
        public int VoiceRetries = 1;

        // ── 시계: 질문 예산 대신 시간이 흐른다(23:45 시체 발견부터) ──
        public int MeetMinutes = 5;             // 첫 대면 진술
        public int AskMinutes = 5;              // 질문 한 번
        public int PressMinutes = 10;           // 추궁
        public int WrongPresentMinutes = 10;    // 틀린 모순 제시(무작위 찍기 방지)
        public bool SecondMurder = true;
        public int SecondMurderAt = 60;         // 00:45 — 범인이 다시 움직인다
        public int DeadlineAt = 120;            // 01:45 — 범인이 달아나려 한다(이때부턴 지목만 가능)
    }

    public enum PressReaction { Maintain, Confess, Evade }

    public sealed class ContradictionRecord
    {
        public int Id;
        public CellRef A, B;
        public ContradictionKind Kind;
        public string Explanation;
        public readonly Dictionary<string, PressReaction> Reactions = new Dictionary<string, PressReaction>();

        public bool Involves(string person) => A.Person == person || B.Person == person;
        public bool Touches(string person, int slot) => (A.Person == person && A.Slot == slot) || (B.Person == person && B.Slot == slot);
        public string Label => $"{A.Person} {AlibiCase.TimeLabel(A.Slot)} × {B.Person} {AlibiCase.TimeLabel(B.Slot)}";
    }

    /// <summary>보드 한 칸의 표시 상태(뷰는 이것만 보고 그린다).</summary>
    public struct CellState
    {
        public bool LocationKnown, SeenKnown;
        public string Room;
        public IReadOnlyList<string> Seen;
        public string CorrectedFrom;   // 고백(또는 유품 수첩)으로 바뀐 칸: 원래 댔던 방
        public bool InContradiction;
        public bool Evaded;            // 추궁에 해명하지 못함
        public bool Maintained;        // 추궁에도 진술을 지킴
    }

    public sealed class PresentOutcome
    {
        public bool Valid, Duplicate;
        public int MinutesLost;
        public string Message;
        public ContradictionRecord Record;
    }

    public sealed class PressOutcome
    {
        public PressReaction Reaction;
        public string Line;
    }

    public sealed class AccuseOutcome
    {
        public bool Correct;
        public string Accused;
        public string Text;
    }

    public enum SessionEventKind { SecondMurder, Deadline }

    /// <summary>시계가 흐르다 터지는 사건. 뷰는 이걸 꺼내 컷신으로 보여 준다.</summary>
    public sealed class SessionEvent
    {
        public SessionEventKind Kind;
        public string Victim;
        public string Room;
        public string Note;            // 희생자 수첩에 남은 글
        public CellRef? Revealed;      // 수첩으로 드러난 칸
    }

    /// <summary>
    /// 한 판의 진행. 질문 → (해석) → 엔진이 답할 사실을 정함 → 보드 기록 → LLM이 말투로 옮김(검증·재시도·템플릿 대체).
    /// 모순 판정·추궁 반응·두 번째 살인·승패는 전부 코드가 정한다. LLM이 죽어도 템플릿으로 끝까지 플레이된다.
    /// </summary>
    public sealed class AlibiSession
    {
        public readonly AlibiCase Case;
        public readonly SessionOptions Options;
        readonly ILlm _llm;

        readonly bool[,] _loc;
        readonly bool[,] _seen;
        readonly Dictionary<string, int> _lastSlot = new Dictionary<string, int>();
        readonly HashSet<string> _met = new HashSet<string>();
        readonly Dictionary<string, HashSet<IntentTopic>> _askedTopics = new Dictionary<string, HashSet<IntentTopic>>();
        bool _deadlineAnnounced;

        public readonly Dictionary<string, List<(string Who, string Text)>> Chats = new Dictionary<string, List<(string, string)>>();
        public readonly List<ContradictionRecord> Contradictions = new List<ContradictionRecord>();
        public readonly List<string> Log = new List<string>();
        public readonly Queue<SessionEvent> Events = new Queue<SessionEvent>();

        public int Clock { get; private set; }          // 23:45부터 흐른 분
        public int Actions { get; private set; }        // 쓴 행동 수(결과 화면용)
        public bool SecondMurderDone { get; private set; }
        public string SecondVictim { get; private set; }
        public bool Over { get; private set; }
        public AccuseOutcome Result { get; private set; }
        public event Action Changed;

        // 디버그(하네스·화면 하단 표시용)
        public string LastIntent, LastSource, LastProblem, LastError;

        public const string Detective = "탐정";
        public const string Opener = "오늘 밤 어디에 있었는지, 처음부터 말해 주시오.";
        const int ClockOrigin = 23 * 60 + 45;

        public AlibiSession(AlibiCase c, ILlm llm, SessionOptions options = null)
        {
            Case = c;
            _llm = llm;
            Options = options ?? new SessionOptions();
            _loc = new bool[c.Suspects.Count, c.SlotCount];
            _seen = new bool[c.Suspects.Count, c.SlotCount];
            for (int i = 0; i < c.Suspects.Count; i++) _loc[i, AlibiCase.MurderSlot] = true; // 비명 때 위치는 모두 이미 말했다
            foreach (var s in c.Suspects)
            {
                Chats[s.Name] = new List<(string, string)>();
                _askedTopics[s.Name] = new HashSet<IntentTopic>();
            }
            Log.Add("비명이 들렸을 때(23:15) 어디 있었는지는 모두 이미 말했다.");
        }

        public bool LlmEnabled => Options.UseLlm && _llm != null;

        // ───────────── 시계 ─────────────

        public static string FormatClock(int elapsed)
        {
            int m = (ClockOrigin + elapsed) % (24 * 60);
            return $"{m / 60:00}:{m % 60:00}";
        }

        public string ClockLabel => FormatClock(Clock);
        public bool DeadlineReached => Clock >= Options.DeadlineAt;
        /// <summary>질문·추궁·제시를 할 수 있나(마감 뒤엔 지목만).</summary>
        public bool CanAct => !Over && !DeadlineReached;
        public int MinutesToDeadline => Math.Max(0, Options.DeadlineAt - Clock);
        public int MinutesToSecondMurder => Options.SecondMurder && !SecondMurderDone ? Math.Max(0, Options.SecondMurderAt - Clock) : -1;
        public bool IsDead(string name) => Case.Get(name)?.Dead ?? false;
        public bool HasMet(string name) => _met.Contains(name);

        void Spend(int minutes)
        {
            Clock += minutes;
            Actions++;
            if (Options.SecondMurder && !SecondMurderDone && Clock >= Options.SecondMurderAt) DoSecondMurder();
            if (!_deadlineAnnounced && Clock >= Options.DeadlineAt)
            {
                _deadlineAnnounced = true;
                Log.Add("범인이 저택을 빠져나가려 한다. 이제 지목만 남았다.");
                Events.Enqueue(new SessionEvent { Kind = SessionEventKind.Deadline });
            }
        }

        // ───────────── 보드 ─────────────

        public CellState Cell(int i, int t)
        {
            var p = Case.Suspects[i];
            var cl = p.Claims[t];
            var st = new CellState
            {
                LocationKnown = _loc[i, t],
                SeenKnown = _seen[i, t],
                Room = _loc[i, t] ? cl.Room : null,
                Seen = _seen[i, t] ? cl.Seen : null,
            };
            if (p.Confessed && p.OriginalClaims[t].Room != cl.Room) st.CorrectedFrom = p.OriginalClaims[t].Room;
            foreach (var r in Contradictions)
            {
                if (!r.Touches(p.Name, t)) continue;
                st.InContradiction = true;
                if (r.Reactions.TryGetValue(p.Name, out var re))
                {
                    if (re == PressReaction.Evade) st.Evaded = true;
                    if (re == PressReaction.Maintain) st.Maintained = true;
                }
            }
            return st;
        }

        public StatementView View(CellRef r)
        {
            int i = Case.IndexOf(r.Person);
            var cl = Case.Suspects[i].Claims[r.Slot];
            return new StatementView
            {
                Person = r.Person, Slot = r.Slot,
                Room = _loc[i, r.Slot] ? cl.Room : null,
                SeenKnown = _seen[i, r.Slot],
                Seen = cl.Seen,
            };
        }

        public bool IsKnown(CellRef r) => _loc[Case.IndexOf(r.Person), r.Slot];

        void Reveal(CellRef r, bool seen)
        {
            int i = Case.IndexOf(r.Person);
            _loc[i, r.Slot] = true;
            if (seen) _seen[i, r.Slot] = true;
        }

        // ───────────── 첫 대면: 스스로 털어놓는 진술 ─────────────

        /// <summary>
        /// 처음 마주하면 그 사람이 밤새 동선을 스스로 털어놓는다 — "밤새 어디 있었어?"를 다섯 번 치는 반복 노동을 없앤다.
        /// 위치만 밝히고, 누구를 봤는지는 질문해야 나온다.
        /// </summary>
        public async Task<string> MeetAsync(string suspect, CancellationToken ct = default)
        {
            var p = Case.Get(suspect);
            if (p == null || p.Dead || !CanAct || _met.Contains(suspect)) return null;
            _met.Add(suspect);

            var chat = Chats[suspect];
            var history = chat.ToList();
            chat.Add((Detective, Opener));

            var sheet = CharacterSheets.Get(suspect);
            var plan = new AnswerPlan { Stance = StanceFor(p) };
            var fb = new StringBuilder();
            plan.FactLines.Add("탐정에게 처음 하는 진술이다. 시간 순서대로, 시각마다 짧게 말한다.");
            for (int t = 0; t < Case.SlotCount; t++)
            {
                var cl = p.Claims[t];
                plan.FactLines.Add($"{AlibiCase.SpokenTime(t)}에는 {cl.Room}에 있었다.");
                plan.AllowedRooms.Add(cl.Room);
                plan.MustRooms.Add(cl.Room);
                plan.AllowedSlots.Add(t);
                Reveal(new CellRef(suspect, t), false);
                fb.Append(CharacterSheets.SayLocation(sheet.Speech, t, cl.Room)).Append(' ');
            }
            if (p.IsSecretLiar && !p.Confessed) plan.AllowedSlots.Add(p.SecretSlot);
            plan.Sentences = Case.SlotCount + 1;
            plan.MaxTokens = 60 + 35 * Case.SlotCount;
            plan.Fallback = fb.ToString().Trim();
            _lastSlot[suspect] = AlibiCase.MurderSlot;

            var line = await VoiceAsync(p, Opener, history, plan, ct);
            chat.Add((suspect, line));
            Spend(Options.MeetMinutes);
            Changed?.Invoke();
            return line;
        }

        /// <summary>자주 쓰는 질문 버튼(누르면 그대로 질문한다). 자유 입력은 떠보거나 몰아붙일 때만 쓰면 된다.</summary>
        public List<(string Label, string Question)> QuickQuestions(string suspect)
        {
            var list = new List<(string, string)>();
            var p = Case.Get(suspect);
            if (p == null || p.Dead || !_met.Contains(suspect)) return list;
            int i = Case.IndexOf(suspect);
            for (int t = 0; t < Case.SlotCount; t++)
            {
                if (!_loc[i, t] || _seen[i, t]) continue;
                string room = p.Claims[t].Room;
                list.Add(($"{AlibiCase.TimeLabel(t)} 누굴 봤나", $"{AlibiCase.SpokenTime(t)}에 {room}에서 누구 봤어?"));
            }
            var asked = _askedTopics[suspect];
            if (!asked.Contains(IntentTopic.Victim)) list.Add(("피해자와 관계", $"{Josa.WaGwa(Case.Victim)}는 어떤 사이였어?"));
            if (!asked.Contains(IntentTopic.Opinion)) list.Add(("수상한 사람?", "이 중에 누가 제일 수상하다고 생각해?"));
            return list;
        }

        // ───────────── 질문 ─────────────

        /// <param name="quick">질문 버튼에서 온 질문이면 규칙 해석만 쓴다(빠르고 확실하다).</param>
        public async Task<string> AskAsync(string suspect, string question, CancellationToken ct = default, bool quick = false)
        {
            var p = Case.Get(suspect);
            if (p == null || p.Dead || !CanAct || string.IsNullOrWhiteSpace(question)) return null;
            question = question.Trim();

            var chat = Chats[suspect];
            var history = chat.ToList();
            chat.Add((Detective, question));

            int last = _lastSlot.TryGetValue(suspect, out var ls) ? ls : -1;
            var intent = await ParseIntentAsync(p, question, history, last, quick, ct);
            LastIntent = intent.ToString();
            _askedTopics[suspect].Add(intent.Topic);

            var plan = PlanAnswer(p, intent, last);
            foreach (var r in plan.RevealLocation) Reveal(r, false);
            foreach (var r in plan.RevealSeen) Reveal(r, true);
            if (plan.FocusSlot >= 0) _lastSlot[suspect] = plan.FocusSlot;

            var line = await VoiceAsync(p, question, history, plan, ct);
            chat.Add((suspect, line));
            Spend(Options.AskMinutes);
            Changed?.Invoke();
            return line;
        }

        async Task<QuestionIntent> ParseIntentAsync(Suspect p, string question, List<(string Who, string Text)> history, int last, bool quick, CancellationToken ct)
        {
            var rx = IntentParser.ParseRegex(question, Case);
            if (!LlmEnabled || quick) return rx;
            // 시각이 박혀 있고 무엇을 묻는지 분명하면 규칙 해석으로 충분하다(LLM 한 번을 아껴 대답이 빨라진다)
            if (rx.Topic == IntentTopic.Alibi && rx.Slots.Count > 0 && (rx.AskSeen || rx.AskLocation)) { rx.Source = "regex-fast"; return rx; }

            string lastQ = null, lastA = null;
            for (int i = history.Count - 1; i >= 1; i--)
                if (history[i].Who == p.Name && history[i - 1].Who == Detective) { lastQ = history[i - 1].Text; lastA = history[i].Text; break; }
            try
            {
                var pr = AlibiPrompts.Intent(Case, p.Name, question, lastQ, lastA, last);
                var json = await _llm.GenerateAsync(pr.System, pr.User, Options.IntentTemperature, true, Options.IntentMaxTokens, ct);
                return IntentParser.Merge(IntentParser.FromJson(json, Case), rx);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                LastError = e.Message;
                return rx;
            }
        }

        AnswerPlan PlanAnswer(Suspect p, QuestionIntent it, int last)
        {
            var sheet = CharacterSheets.Get(p.Name);
            var plan = new AnswerPlan { Stance = StanceFor(p) };
            switch (it.Topic)
            {
                case IntentTopic.Alibi:
                    PlanAlibi(p, sheet, it, last, plan);
                    break;
                case IntentTopic.Victim:
                    plan.NoPlaceTime = true;
                    plan.FactLines.Add("피해자와의 관계: " + p.Relation);
                    plan.Fallback = sheet.Speech == Speech.Banmal ? p.Relation.Replace("다.", "어.") : p.Relation;
                    break;
                case IntentTopic.Opinion:
                    plan.NoPlaceTime = true;
                    plan.FactLines.Add("누가 범인인지 확실한 건 모른다. 근거 없이 누군가를 지목하지는 않되, 네 성격대로 한마디 해도 된다.");
                    plan.Fallback = CharacterSheets.Shrug(sheet.Speech);
                    break;
                case IntentTopic.Accuse:
                    plan.NoPlaceTime = true;
                    plan.FactLines.Add(p.IsKiller ? "범인으로 의심받았다. 범행을 부정한다." : "범인으로 의심받았다. 너는 범인이 아니다.");
                    plan.Fallback = CharacterSheets.Deny(sheet.Speech);
                    break;
                default:
                    plan.NoPlaceTime = true;
                    plan.FactLines.Add("사건과 상관없는 질문이다. 짧게 받아친다.");
                    plan.Fallback = CharacterSheets.Shrug(sheet.Speech);
                    break;
            }
            return plan;
        }

        void PlanAlibi(Suspect p, CharacterSheet sheet, QuestionIntent it, int last, AnswerPlan plan)
        {
            var slots = new List<int>(it.Slots);
            bool askSeen = it.AskSeen;
            string note = null;
            var fb = new StringBuilder();

            if (slots.Count == 0)
            {
                if (it.Relative != 0)
                {
                    int baseSlot = last >= 0 ? last : AlibiCase.MurderSlot;
                    slots.Add(Math.Max(0, Math.Min(Case.SlotCount - 1, baseSlot + it.Relative)));
                }
                else if (it.AboutRoom != null)
                {
                    for (int t = 0; t < Case.SlotCount; t++) if (p.Claims[t].Room == it.AboutRoom) slots.Add(t);
                    if (slots.Count == 0)
                    {
                        // 그 방엔 간 적이 없다 → 대신 밤새 어디 있었는지 말한다(보드에 쓸 수 있는 진술로)
                        note = $"오늘 밤 {it.AboutRoom}에는 간 적이 없다.";
                        plan.AllowedRooms.Add(it.AboutRoom);
                        for (int t = 0; t < Case.SlotCount; t++) slots.Add(t);
                        askSeen = false;
                    }
                }
                else if (it.AboutPerson != null && askSeen && it.AboutPerson != p.Name)
                {
                    for (int t = 0; t < Case.SlotCount; t++) if (p.Claims[t].Seen.Contains(it.AboutPerson)) slots.Add(t);
                    if (slots.Count == 0)
                    {
                        plan.FactLines.Add($"오늘 밤 {Josa.EulReul(it.AboutPerson)} 본 적이 없다.");
                        plan.AllowedPersons.Add(it.AboutPerson);
                        plan.Fallback = CharacterSheets.NeverSaw(sheet.Speech, it.AboutPerson);
                        return;
                    }
                }
                else if (it.AllSlots)
                {
                    for (int t = 0; t < Case.SlotCount; t++) slots.Add(t);
                }
                else slots.Add(last >= 0 ? last : AlibiCase.MurderSlot);
            }

            // 사람이라면 밤새 누구를 봤는지 한꺼번에 읊지 않는다 — 시각을 짚어 물어야 한다(정보 경제)
            bool tooMany = askSeen && slots.Count > 2;
            if (tooMany) askSeen = false;

            if (note != null) plan.FactLines.Add(note);
            foreach (int t in slots.Distinct().OrderBy(x => x))
            {
                var cl = p.Claims[t];
                string time = AlibiCase.SpokenTime(t);
                plan.AllowedSlots.Add(t);
                plan.AllowedRooms.Add(cl.Room);
                plan.MustRooms.Add(cl.Room);
                plan.FactLines.Add($"{time}에는 {cl.Room}에 있었다.");
                plan.RevealLocation.Add(new CellRef(p.Name, t));
                fb.Append(CharacterSheets.SayLocation(sheet.Speech, t, cl.Room)).Append(' ');
                if (askSeen)
                {
                    plan.RevealSeen.Add(new CellRef(p.Name, t));
                    if (cl.Seen.Count == 0) plan.FactLines.Add($"{time}에 {cl.Room}에서 너는 아무도 보지 못했다(혼자였다).");
                    else plan.FactLines.Add($"{time}에 {cl.Room}에서 본 사람: {Names(cl.Seen)}.");
                    foreach (var n in cl.Seen) { plan.AllowedPersons.Add(n); plan.MustPersons.Add(n); }
                    fb.Append(CharacterSheets.SaySeen(sheet.Speech, cl.Seen, Case.Victim)).Append(' ');
                }
            }
            if (tooMany)
            {
                plan.FactLines.Add("누구를 봤는지는 시각을 하나 콕 집어서 물어보라고 한다.");
                fb.Append(CharacterSheets.AskForTime(sheet.Speech));
            }
            plan.FocusSlot = slots.Max();
            plan.Fallback = fb.ToString().Trim();
            // 레드헤링이 비밀 시각을 얼버무리며 '그때는 기억 안 나' 하는 건 좋은 단서다 — 시각만 말하는 건 허용(방을 대면 검증에 걸린다)
            if (p.IsSecretLiar && !p.Confessed) plan.AllowedSlots.Add(p.SecretSlot);
            int slotCount = slots.Distinct().Count();
            plan.Sentences = Math.Max(2, slotCount + (note != null ? 1 : 0));
            plan.MaxTokens = 60 + 35 * slotCount;
        }

        string Names(IEnumerable<string> names) =>
            string.Join(", ", names.Select(n => n == Case.Victim ? n + "(피해자)" : n));

        string StanceFor(Suspect p)
        {
            if (p.IsKiller)
                return "너는 사실 범인이다. 하지만 절대 들키면 안 된다. [말할 내용]이 네 공식 진술이니 그대로 말하고 다른 것은 덧붙이지 않는다. 침착한 척한다.";
            if (p.IsSecretLiar && !p.Confessed)
                return $"너는 범인이 아니다. 다만 살인과 무관하게 숨기고 싶은 일이 있다. [말할 내용]이 네 진술이니 그대로 말한다. {AlibiCase.SpokenTime(p.SecretSlot)} 이야기가 나오면 살짝 불편해한다.";
            return "너는 범인이 아니다. 기억나는 대로 솔직하게 말한다.";
        }

        async Task<string> VoiceAsync(Suspect p, string question, List<(string Who, string Text)> history, AnswerPlan plan, CancellationToken ct)
        {
            LastProblem = null;
            if (!LlmEnabled) { LastSource = "template"; return plan.Fallback; }

            var pr = AlibiPrompts.Reply(Case, p, question, history, plan);
            int maxSentences = plan.Sentences;
            int maxTokens = plan.MaxTokens > 0 ? plan.MaxTokens : Options.VoiceMaxTokens;
            string extra = "";
            for (int attempt = 0; attempt <= Options.VoiceRetries; attempt++)
            {
                string raw;
                try { raw = await _llm.GenerateAsync(pr.System, pr.User + extra, Options.VoiceTemperature, false, maxTokens, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { LastError = e.Message; break; }

                var line = AlibiPrompts.Clean(raw, p.Name, maxSentences);
                var problem = AlibiPrompts.Validate(line, plan, p.Name, question, Case);
                if (problem == null)
                {
                    LastSource = attempt == 0 ? "llm" : "llm-retry";
                    return line;
                }
                LastProblem = problem + " ← \"" + line + "\"";
                extra = AlibiPrompts.Correction(problem);
            }
            LastSource = "template";
            return plan.Fallback;
        }

        // ───────────── 모순 제시 ─────────────

        public PresentOutcome Present(CellRef a, CellRef b)
        {
            if (!CanAct) return null;
            if (!IsKnown(a) || !IsKnown(b))
                return new PresentOutcome { Valid = false, Message = "아직 드러나지 않은 진술이다. 먼저 물어봐야 한다." };
            var v = ContradictionRules.Check(View(a), View(b));
            if (!v.Valid)
            {
                Log.Add("모순 아님: " + v.Hint);
                Spend(Options.WrongPresentMinutes);
                Changed?.Invoke();
                return new PresentOutcome { Valid = false, MinutesLost = Options.WrongPresentMinutes, Message = v.Hint };
            }
            var dup = Contradictions.FirstOrDefault(r => (r.Touches(a.Person, a.Slot) && r.Touches(b.Person, b.Slot)));
            if (dup != null) return new PresentOutcome { Valid = true, Duplicate = true, Record = dup, Message = "이미 찾은 모순이다." };

            var rec = new ContradictionRecord { Id = Contradictions.Count + 1, A = a, B = b, Kind = v.Kind, Explanation = v.Explanation };
            Contradictions.Add(rec);
            Log.Add("모순 성립 — " + v.Explanation);
            Changed?.Invoke();
            return new PresentOutcome { Valid = true, Record = rec, Message = v.Explanation };
        }

        /// <summary>이 사람이 이 모순에 대해 아직 추궁받지 않았나(죽은 사람은 추궁할 수 없다).</summary>
        public bool CanPress(string person, ContradictionRecord r) =>
            CanAct && !IsDead(person) && r.Involves(person) && !r.Reactions.ContainsKey(person);

        public async Task<PressOutcome> PressAsync(string suspect, int recordId, CancellationToken ct = default)
        {
            var rec = Contradictions.FirstOrDefault(r => r.Id == recordId);
            if (rec == null || !CanPress(suspect, rec)) return null;

            var p = Case.Get(suspect);
            var sheet = CharacterSheets.Get(suspect);
            var mine = rec.A.Person == suspect ? rec.A : rec.B;
            var other = rec.A.Person == suspect ? rec.B : rec.A;
            bool atFault;
            if (rec.A.Person == rec.B.Person)
            {
                // 자기모순(같은 사람의 두 진술): 어느 쪽이 거짓인지 골라 그 칸을 기준으로 반응한다
                bool aBad = IsAtFault(rec.A, rec.B), bBad = IsAtFault(rec.B, rec.A);
                atFault = aBad || bBad;
                if (bBad && !aBad) { mine = rec.B; other = rec.A; }
            }
            else atFault = IsAtFault(mine, other);

            PressReaction reaction;
            if (!atFault) reaction = PressReaction.Maintain;
            else if (p.IsSecretLiar && !p.Confessed) reaction = PressReaction.Confess;
            else if (p.IsKiller) reaction = PressReaction.Evade;
            else reaction = PressReaction.Maintain;

            var chat = Chats[suspect];
            var history = chat.ToList();
            string question = $"이 진술, 앞뒤가 안 맞는데. ({rec.Label}) 설명해 봐.";
            chat.Add((Detective, question));

            var plan = new AnswerPlan();
            switch (reaction)
            {
                case PressReaction.Confess:
                {
                    int ts = p.SecretSlot;
                    string old = p.Claims[ts].Room;
                    ConfessSecret(p);

                    var truth = p.Claims[ts];
                    string time = AlibiCase.SpokenTime(ts);
                    plan.Stance = "탐정이 네 진술의 모순을 들이밀었다. 더는 숨길 수 없어 비밀을 털어놓는다. 부끄러움이나 억울함을 네 말투로 드러낸다. 너는 범인이 아니다.";
                    plan.FactLines.Add($"{time}에 사실은 {Josa.IGa(old)} 아니라 {truth.Room}에 있었다.");
                    plan.FactLines.Add($"거기서 한 일: {p.Secret}.");
                    plan.FactLines.Add(truth.Seen.Count == 0 ? $"{time}에 {truth.Room}에서 너는 아무도 보지 못했다(혼자였다)." : $"{time}에 {truth.Room}에서 본 사람: {Names(truth.Seen)}.");
                    plan.FactLines.Add("살인과는 무관하다.");
                    plan.AllowedRooms.Add(old); plan.AllowedRooms.Add(truth.Room); plan.MustRooms.Add(truth.Room);
                    plan.AllowedSlots.Add(ts);
                    plan.Sentences = 3;
                    plan.MaxTokens = 170;
                    foreach (var n in truth.Seen) { plan.AllowedPersons.Add(n); plan.MustPersons.Add(n); }
                    plan.Fallback = CharacterSheets.Confess(sheet.Speech, ts, truth.Room, p.Secret) + " " + CharacterSheets.SaySeen(sheet.Speech, truth.Seen, Case.Victim);
                    Log.Add($"{Josa.EunNeun(p.Name)} 비밀을 털어놓았다 — {AlibiCase.TimeLabel(ts)}엔 사실 {truth.Room}에 있었다.");
                    break;
                }
                case PressReaction.Evade:
                    plan.NoPlaceTime = true;
                    plan.Stance = StanceFor(p);
                    plan.FactLines.Add("탐정이 네 진술의 모순을 정확히 짚었다. 새로운 사실은 절대 말하지 말고, 기억이 안 난다거나 착각했다고 얼버무리거나 말을 돌린다.");
                    plan.Fallback = CharacterSheets.Evade(sheet.Speech);
                    Log.Add($"{Josa.EunNeun(p.Name)} 모순을 해명하지 못했다.");
                    break;
                default:
                {
                    var cl = p.Claims[mine.Slot];
                    var view = View(mine);
                    string time = AlibiCase.SpokenTime(mine.Slot);
                    plan.Stance = StanceFor(p) + " 탐정이 네 진술이 다른 사람 말과 안 맞는다고 했다. 너는 본 대로 말했다고 확신을 담아 다시 말한다.";
                    plan.FactLines.Add($"{time}에는 {cl.Room}에 있었다.");
                    if (view.SeenKnown)
                        plan.FactLines.Add(cl.Seen.Count == 0 ? $"{time}에 {cl.Room}에서 너는 아무도 보지 못했다(혼자였다)." : $"{time}에 {cl.Room}에서 본 사람: {Names(cl.Seen)}.");
                    plan.FactLines.Add($"네 진술은 사실이다. 틀린 건 {Josa.UiOf(other.Person)} 말이다.");
                    plan.AllowedRooms.Add(cl.Room); plan.MustRooms.Add(cl.Room); plan.AllowedSlots.Add(mine.Slot);
                    foreach (var n in cl.Seen) plan.AllowedPersons.Add(n);
                    plan.AllowedPersons.Add(other.Person);
                    plan.Fallback = CharacterSheets.Maintain(sheet.Speech) + " " + CharacterSheets.SayLocation(sheet.Speech, mine.Slot, cl.Room);
                    Log.Add($"{Josa.EunNeun(p.Name)} 자기 진술을 지켰다({AlibiCase.TimeLabel(mine.Slot)} {cl.Room}).");
                    break;
                }
            }
            rec.Reactions[suspect] = reaction;

            var line = await VoiceAsync(p, question, history, plan, ct);
            chat.Add((suspect, line));
            Spend(Options.PressMinutes);
            Changed?.Invoke();
            return new PressOutcome { Reaction = reaction, Line = line };
        }

        /// <summary>레드헤링의 비밀을 진실로 바꾼다(추궁으로 털어놓거나, 죽은 뒤 수첩으로 드러날 때).</summary>
        void ConfessSecret(Suspect p)
        {
            int ts = p.SecretSlot;
            p.Confessed = true;
            for (int t = 0; t < Case.SlotCount; t++) p.Claims[t] = p.Truth(t);
            Reveal(new CellRef(p.Name, ts), true);
            // 같은 비밀 진술에 걸린 다른 모순들도 이로써 풀렸다
            foreach (var r in Contradictions)
                if (r.Touches(p.Name, ts) && !r.Reactions.ContainsKey(p.Name)) r.Reactions[p.Name] = PressReaction.Confess;
        }

        /// <summary>내 진술만 진실로 바꿨을 때 모순이 풀리면, 이 모순의 원인은 내 쪽이다.</summary>
        bool IsAtFault(CellRef mine, CellRef other)
        {
            var vm = View(mine);
            var vo = View(other);
            bool fixedByMine = !ContradictionRules.Check(TruthView(mine, vm), vo).Valid;
            bool fixedByOther = !ContradictionRules.Check(vm, TruthView(other, vo)).Valid;
            if (fixedByMine != fixedByOther) return fixedByMine;
            var p = Case.Get(mine.Person);
            return p.Claims[mine.Slot].Room != p.TruePath[mine.Slot] || (vm.SeenKnown && p.LiesAt(mine.Slot));
        }

        StatementView TruthView(CellRef r, StatementView revealed)
        {
            var tr = Case.Get(r.Person).Truth(r.Slot);
            return new StatementView { Person = r.Person, Slot = r.Slot, Room = tr.Room, SeenKnown = revealed.SeenKnown, Seen = tr.Seen };
        }

        // ───────────── 두 번째 살인 ─────────────

        /// <summary>
        /// 범인이 '자기에게 가장 위험한 증인'을 죽인다 — 그 사람의 진실이 범인의 거짓과 정면으로 부딪히는 사람.
        /// 다만 사건이 풀 수 없게 되면 안 되니, 희생자의 수첩이 그 결정적 진술을 남긴다(죽은 자의 증언).
        /// </summary>
        void DoSecondMurder()
        {
            SecondMurderDone = true;
            var k = Case.Killer;
            var cands = Case.Suspects.Where(s => !s.IsKiller && !s.Dead).ToList();
            if (cands.Count == 0) return;

            var killerLies = Enumerable.Range(0, Case.SlotCount).Where(t => k.LiesAt(t))
                .Select(t => new StatementView { Person = k.Name, Slot = t, Room = k.Claims[t].Room, SeenKnown = true, Seen = k.Claims[t].Seen }).ToList();
            Suspect victim = null;
            int bestSlot = AlibiCase.MurderSlot, bestScore = 0;
            var rng = new Random(Case.Seed * 31 + 7);
            foreach (var c in cands.OrderBy(_ => rng.Next()))
            {
                for (int t = 0; t < Case.SlotCount; t++)
                {
                    var tr = c.Truth(t);
                    var tv = new StatementView { Person = c.Name, Slot = t, Room = tr.Room, SeenKnown = true, Seen = tr.Seen };
                    int hits = killerLies.Count(kv => ContradictionRules.Check(tv, kv).Valid);
                    if (hits == 0) continue;
                    // 아직 탐정이 듣지 못한 진술일수록 범인에게 위험하다
                    int score = hits * (_seen[Case.IndexOf(c.Name), t] ? 1 : 3);
                    if (score > bestScore) { bestScore = score; victim = c; bestSlot = t; }
                }
            }
            if (victim == null) victim = cands[rng.Next(cands.Count)];

            victim.Dead = true;
            SecondVictim = victim.Name;
            if (victim.IsSecretLiar && !victim.Confessed && bestScore == 0) bestSlot = victim.SecretSlot;

            // 수첩에는 밤새 동선이 적혀 있다
            for (int t = 0; t < Case.SlotCount; t++) Reveal(new CellRef(victim.Name, t), false);

            // 이 증인 없이도 범인을 잡을 길이 남았으면 수첩은 '어디를 볼지'만 가리킨다(답을 대신 주면 추리가 사라진다).
            // 길이 끊겼을 때만 결정적 진술을 남긴다 — 어떤 경우에도 풀 수 없는 사건이 되면 안 된다.
            var note = new StringBuilder();
            string keyRoom = victim.TruePath[bestSlot];
            if (KillerStillCatchable())
            {
                note.Append($"{AlibiCase.TimeLabel(bestSlot)} {keyRoom}… 그때 분명 뭔가 봤다. (뒷부분은 피에 번져 읽을 수 없다)");
            }
            else
            {
                string secretLine = null;
                if (victim.IsSecretLiar && !victim.Confessed)
                {
                    ConfessSecret(victim);
                    secretLine = victim.Secret;
                }
                Reveal(new CellRef(victim.Name, bestSlot), true);
                var cl = victim.Claims[bestSlot];
                note.Append($"{AlibiCase.TimeLabel(bestSlot)} {cl.Room}. ");
                note.Append(cl.Seen.Count == 0 ? "아무도 없었다." : $"{Josa.EulReul(string.Join(", ", cl.Seen))} 봤다.");
                if (secretLine != null) note.Append($" ({secretLine}.)");
            }
            string room = victim.TruePath[Case.SlotCount - 1];

            Log.Add($"두 번째 비명 — {Josa.IGa(victim.Name)} {room}에서 숨진 채 발견됐다. 수첩에 '{note}'라고 적혀 있었다.");
            Events.Enqueue(new SessionEvent
            {
                Kind = SessionEventKind.SecondMurder,
                Victim = victim.Name,
                Room = room,
                Note = note.ToString(),
                Revealed = new CellRef(victim.Name, bestSlot),
            });
        }

        /// <summary>
        /// 앞으로 들을 수 있는 진술(산 사람은 전부, 죽은 사람은 이미 드러난 칸만)로 범인의 거짓말과 부딪히는 모순이 아직 있나.
        /// </summary>
        bool KillerStillCatchable()
        {
            var k = Case.Killer;
            var views = new List<StatementView>();
            for (int i = 0; i < Case.Suspects.Count; i++)
            {
                var p = Case.Suspects[i];
                for (int t = 0; t < Case.SlotCount; t++)
                {
                    if (p.Dead)
                    {
                        if (!_loc[i, t]) continue;
                        views.Add(View(new CellRef(p.Name, t)));
                    }
                    else
                    {
                        var cl = p.Claims[t];
                        views.Add(new StatementView { Person = p.Name, Slot = t, Room = cl.Room, SeenKnown = true, Seen = cl.Seen });
                    }
                }
            }
            foreach (var (a, b, _) in ContradictionRules.FindAll(views))
            {
                if (a.Person == k.Name && k.LiesAt(a.Slot)) return true;
                if (b.Person == k.Name && k.LiesAt(b.Slot)) return true;
            }
            return false;
        }

        // ───────────── 지목 ─────────────

        public AccuseOutcome Accuse(string suspect)
        {
            if (Over) return Result;
            var p = Case.Get(suspect);
            if (p == null || p.Dead) return null;
            Over = true;
            bool ok = p.IsKiller;
            Result = new AccuseOutcome { Correct = ok, Accused = suspect, Text = BuildResult(ok, suspect) };
            Changed?.Invoke();
            return Result;
        }

        public string Briefing()
        {
            var c = Case;
            var o = Options;
            var sb = new StringBuilder();
            sb.AppendLine("밤 11시 15분, 저택에 비명이 울렸다.");
            sb.AppendLine($"23시 45분, 하인이 {c.MurderRoom}에서 {Josa.UiOf(c.Victim)} 시체를 발견했다. 흉기는 {c.Weapon} — 원래 {c.WeaponRoom}에 있던 물건이다.");
            sb.AppendLine("용의자는 다섯. 범인은 아직 이 안에 있고, 다시 죽일 것이다.");
            sb.AppendLine();
            sb.AppendLine("<b>규칙</b>");
            sb.AppendLine("① 15분마다 복도 한 칸까지만 움직일 수 있다.");
            sb.AppendLine("② 같은 시각 같은 방에 있었다면 반드시 서로 봤다.");
            sb.AppendLine("③ 범인은 거짓말을 한다. 살인과 상관없는 비밀 때문에 거짓말하는 사람도 한 명 더 있다.");
            sb.AppendLine();
            sb.AppendLine("<b>시간</b>");
            sb.AppendLine($"· 대면·질문 {o.AskMinutes}분, 추궁 {o.PressMinutes}분, 틀린 모순 제시 {o.WrongPresentMinutes}분이 흐른다.");
            if (o.SecondMurder) sb.AppendLine($"· {FormatClock(o.SecondMurderAt)}, 범인이 다시 움직인다.");
            sb.AppendLine($"· {FormatClock(o.DeadlineAt)}가 되면 범인이 달아나려 한다. 그 전에 지목하라.");
            return sb.ToString().TrimEnd();
        }

        string BuildResult(bool ok, string accused)
        {
            var c = Case;
            var k = c.Killer;
            var s = c.SecretLiar;
            var sb = new StringBuilder();
            sb.AppendLine(ok ? $"<b>정답.</b> 범인은 {k.Name}였다." : $"<b>오답.</b> {Josa.EunNeun(accused)} 범인이 아니었다. 범인은 {k.Name}였다.");
            sb.AppendLine();

            var night = new List<string>();
            for (int t = 0; t < c.SlotCount; t++)
            {
                string room = k.TruePath[t];
                string extra = t == c.WeaponSlot && room == c.WeaponRoom ? $"({Josa.EulReul(c.Weapon)} 챙김)" : t == AlibiCase.MurderSlot ? $"({c.Victim} 살해)" : "";
                night.Add($"{AlibiCase.TimeLabel(t)} {room}{extra}");
            }
            sb.AppendLine($"<b>{Josa.UiOf(k.Name)} 밤</b>  " + string.Join(" → ", night));
            var lies = Enumerable.Range(0, c.SlotCount).Where(t => k.OriginalClaims[t].Room != k.TruePath[t])
                .Select(t => $"{AlibiCase.TimeLabel(t)} {k.OriginalClaims[t].Room}").ToList();
            sb.AppendLine($"<b>{Josa.UiOf(k.Name)} 거짓말</b>  " + (lies.Count > 0 ? string.Join(", ", lies) + "에 있었다고 했다." : "본 사람을 숨겼다."));
            var keyK = KeyContradiction(k.Name);
            if (keyK != null) sb.AppendLine("  └ " + keyK);
            sb.AppendLine($"<b>{Josa.UiOf(s.Name)} 비밀</b>  {AlibiCase.TimeLabel(s.SecretSlot)}엔 사실 {s.SecretRoom}에 있었다 — {s.Secret}.");
            if (SecondVictim != null) sb.AppendLine($"<b>두 번째 희생자</b>  {SecondVictim} — 범인에게 가장 위험한 증인이었다.");
            sb.AppendLine();
            sb.AppendLine("<b>모두의 실제 동선</b>");
            foreach (var p in c.Suspects)
                sb.AppendLine($"{p.Name}{(p.IsKiller ? "(범인)" : p.IsSecretLiar ? "(비밀)" : "")}{(p.Dead ? "†" : "")}  " +
                              string.Join(" · ", Enumerable.Range(0, c.SlotCount).Select(t => $"{AlibiCase.TimeLabel(t)} {p.TruePath[t]}")));
            sb.AppendLine();
            bool foundK = Contradictions.Any(r => r.Involves(k.Name) && IsAtFaultOriginal(r, k.Name));
            sb.Append($"지목 시각 {ClockLabel} · 행동 {Actions}번 · 찾은 모순 {Contradictions.Count}개 · 범인의 모순을 {(foundK ? "잡았다" : "놓쳤다")}");
            return sb.ToString();
        }

        bool IsAtFaultOriginal(ContradictionRecord r, string person)
        {
            var mine = r.A.Person == person ? r.A : r.B;
            var p = Case.Get(person);
            return p.OriginalClaims[mine.Slot].Room != p.TruePath[mine.Slot] || r.Reactions.TryGetValue(person, out var re) && re == PressReaction.Evade;
        }

        /// <summary>범인의 거짓말이 깨지는 대표 모순(결과 화면 해설용).</summary>
        string KeyContradiction(string killer)
        {
            var original = Case.Suspects.SelectMany(p => Enumerable.Range(0, Case.SlotCount).Select(t =>
            {
                var cl = p.IsSecretLiar ? p.Truth(t) : p.OriginalClaims[t];
                return new StatementView { Person = p.Name, Slot = t, Room = cl.Room, SeenKnown = true, Seen = cl.Seen };
            })).ToList();
            var k = Case.Get(killer);
            foreach (var (a, b, v) in ContradictionRules.FindAll(original))
            {
                var mine = a.Person == killer ? a : b.Person == killer ? b : default;
                if (mine.Person == null) continue;
                if (k.OriginalClaims[mine.Slot].SameAs(k.Truth(mine.Slot))) continue;
                return v.Explanation;
            }
            return null;
        }
    }
}
