using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MafiaAI.Core;

namespace MafiaAI.LLM
{
    /// <summary>
    /// 밤/낮/투표를 구동한다. 낮에는 저택 방 기반 자유 이동 + 같은 방 1:1 문답으로 진행된다.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Header("진행 설정")]
        public int seed = 0;
        public bool autoStart = true;
        public bool logToConsole = true;
        public bool revealRolesAtStartForDebug = true;

        [Header("인간 좌석")]
        public bool includeHuman = false;
        public int humanSeat = -1;

        public GameConfig config = new GameConfig();

        [NonSerialized] public IActor humanActor;

        public event Action OnGameSetup;
        public event Action<LogEntry> OnLog;
        public event Action<GameState> OnPhaseChanged;
        public event Action<Winner> OnGameEnd;
        public event Action OnLocationsChanged;
        public event Action<string, string, string> OnPlayerQuestion; // asker, room, question

        public Player HumanPlayer { get; private set; }
        public GameState State { get; private set; }

        /// <summary>현재 페이즈가 끝나는 시각(Time.realtimeSinceStartup 기준). 0이면 카운트다운 없음.</summary>
        public float PhaseEndsAt { get; private set; }

        /// <summary>매판 config.RoomCount 기준으로 다시 생성된다("방1".."방N").</summary>
        public string[] Rooms { get; private set; } = { "방1", "방2", "방3", "방4", "방5" };

        OllamaClient _ollama;
        IRng _rng;
        readonly Dictionary<string, IActor> _actors = new();
        readonly Dictionary<string, string> _locations = new();
        readonly List<RoomLine> _roomLines = new();
        readonly Queue<HumanMessage> _humanMsgs = new();
        MansionGenerator.Layout _mansion;
        CancellationTokenSource _cts;

        /// <summary>매판 랜덤 생성된 저택 구조(논리 그래프 + 방 격자 좌표 + 물리 복도 목록).</summary>
        public MansionGenerator.Layout Mansion => _mansion;

        public struct HumanMessage { public string Text; public string Target; public bool Explicit; }
        public struct RoomLine { public int Day; public string Room; public string Speaker; public string Target; public string Text; }

        public string GetPlayerRoom(string id)
        {
            string room;
            return _locations.TryGetValue(id, out room) ? room : null;
        }

        public IReadOnlyDictionary<string, string> Locations => _locations;

        public List<string> GetAliveInRoom(string room)
        {
            return State == null ? new List<string>() : State.Alive.Where(p => GetPlayerRoom(p.Id) == room).Select(p => p.Id).ToList();
        }

        public void MoveHumanToRoom(string room)
        {
            if (State == null || HumanPlayer == null || !HumanPlayer.Alive) return;
            if (!Rooms.Contains(room)) return;
            _locations[HumanPlayer.Id] = room;
            // 밤(공간 사냥) 중 이동은 로그로 노출하지 않는다(은밀함 유지).
            if (State.Phase == Phase.Discuss)
                Emit(LogKind.System, "SYSTEM", HumanPlayer.Id + " 이(가) " + room + "으로 이동했다.");
            OnLocationsChanged?.Invoke();
        }

        /// <summary>인간 마피아가 밤에 직접 살해할 수 있는 대상(생존 비마피아). 마피아가 아니면 빈 목록.</summary>
        public List<string> NightKillCandidates()
        {
            if (State == null || HumanPlayer == null || !HumanPlayer.Alive || HumanPlayer.Role != Role.Mafia)
                return new List<string>();
            return State.Alive.Where(x => x.Role != Role.Mafia).Select(x => x.Id).ToList();
        }

        /// <summary>뷰가 호출: 접근한 대상을 이번 밤 살해 대상으로 확정한다(사망은 새벽에 정산·공개).</summary>
        public bool TrySubmitNightKill(string targetId)
        {
            if (State == null || State.Phase != Phase.Night) return false;
            if (HumanPlayer == null || !HumanPlayer.Alive || HumanPlayer.Role != Role.Mafia) return false;
            if (string.IsNullOrEmpty(targetId) || !NightKillCandidates().Contains(targetId)) return false;
            if (_actors.TryGetValue(HumanPlayer.Id, out var a) && a is HumanActor ha)
            {
                ha.SubmitChoice(targetId);
                return true;
            }
            return false;
        }

        public void SubmitHumanMessage(string text, string target)
        {
            if (State == null || HumanPlayer == null || !HumanPlayer.Alive) return;
            if (State.Phase != Phase.Discuss) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            _humanMsgs.Enqueue(new HumanMessage
            {
                Text = OneSentence(text.Trim()),
                Target = string.IsNullOrEmpty(target) ? null : target,
                Explicit = !string.IsNullOrEmpty(target)
            });
        }

        async void Start()
        {
            if (autoStart) await StartGameAsync();
        }

        void OnDestroy() => _cts?.Cancel();

        public async Task StartGameAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            try { await RunAsync(_cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Setup()
        {
            _rng = new SystemRng(seed == 0 ? (int?)null : seed);
            _ollama = new OllamaClient(config.BaseUrl);
            State = new GameState();
            _actors.Clear();
            _locations.Clear();
            _roomLines.Clear();
            _humanMsgs.Clear();

            int roomCount = Mathf.Clamp(config.RoomCount, 3, 12);
            Rooms = Enumerable.Range(1, roomCount).Select(i => "방" + i).ToArray();

            var personas = PersonaLibrary.PickDistinct(6, _rng);
            for (int i = 0; i < 6; i++)
            {
                var per = personas[i];
                var p = new Player
                {
                    Id = per.Name,
                    IsHuman = false,
                    PersonaName = per.Label,
                    PersonaPrompt = per.Prompt,
                    Temperature = per.Temperature
                };
                State.Players.Add(p);
                _actors[p.Id] = new AIActor(_ollama, config, _rng);
            }

            HumanPlayer = null;
            if (includeHuman)
            {
                int hs = (humanSeat >= 0 && humanSeat < 6) ? humanSeat : _rng.Next(6);
                var hp = State.Players[hs];
                hp.IsHuman = true;
                HumanPlayer = hp;
                if (humanActor != null)
                {
                    _actors[hp.Id] = humanActor;
                    if (humanActor is HumanActor ha) ha.SpatialKillMode = config.SpatialNightHunt;
                }
            }

            GameRules.AssignRoles(State.Players, _rng);
            _mansion = MansionGenerator.Generate(Rooms, _rng);
            if (logToConsole)
                Debug.Log("[DEBUG 저택 구조] 복도: " + string.Join(", ", _mansion.Corridors.Select(e => e.A + "-" + e.B)) +
                          " / 동선 그래프: " + string.Join(" ", _mansion.Adjacency.Select(kv => kv.Key + "→" + string.Join(",", kv.Value))));
            AssignInitialRooms();
        }

        void AssignInitialRooms()
        {
            for (int i = 0; i < State.Players.Count; i++)
                _locations[State.Players[i].Id] = Rooms[_rng.Next(Rooms.Length)];
        }

        async Task RunAsync(CancellationToken ct)
        {
            Setup();
            OnGameSetup?.Invoke();
            OnLocationsChanged?.Invoke();

            Emit(LogKind.System, "SYSTEM", "게임 시작 — 6인 중 마피아는 1명. 저택 안에서 들은 말만 단서가 된다.");
            await _ollama.GenerateAsync(config.Model, "준비됐나?", "한 단어로만 답하라.", 0.1f, false, ct);

            if (revealRolesAtStartForDebug && logToConsole)
                Debug.Log("[DEBUG 역할] " + string.Join(", ", State.Players.Select(p => p.Id + "=" + p.Role.Korean())));

            Winner w = Winner.None;
            for (int guard = 0; guard < config.MaxDays; guard++)
            {
                State.Day++;
                await NightPhase(ct);
                var nr = GameRules.ResolveNight(State);
                DawnPhase(nr);

                w = GameRules.CheckWinner(State);
                if (w != Winner.None) break;

                await DiscussPhase(ct);
                await VotePhase(ct);

                w = GameRules.CheckWinner(State);
                if (w != Winner.None) break;
            }

            SetPhase(Phase.End);
            State.Winner = w;
            string side = w == Winner.Mafia ? "마피아" : (w == Winner.Citizens ? "시민" : "무승부");
            Emit(LogKind.System, "SYSTEM", "게임 종료 — " + side + " 진영 승리!");
            OnGameEnd?.Invoke(w);
        }

        async Task NightPhase(CancellationToken ct)
        {
            SetPhase(Phase.Night);
            Emit(LogKind.System, "SYSTEM", "── Day " + State.Day + " · 밤 (" + config.NightSeconds + "초) ──");
            State.Night.Reset();

            float nightEnd = Time.realtimeSinceStartup + config.NightSeconds;
            PhaseEndsAt = nightEnd;
            var mafia = State.OfRole(Role.Mafia);
            var police = State.OfRole(Role.Police);
            var doctor = State.OfRole(Role.Doctor);

            var jobs = new List<Task>();
            if (mafia != null) jobs.Add(GatherNightInto(mafia, target => State.Night.MafiaTarget = target, nightEnd, ct));
            if (police != null) jobs.Add(GatherNightInto(police, target => State.Night.PoliceTarget = target, nightEnd, ct));
            if (doctor != null) jobs.Add(GatherNightInto(doctor, target => State.Night.DoctorTarget = target, nightEnd, ct));

            int ms = Mathf.Max(0, (int)((nightEnd - Time.realtimeSinceStartup) * 1000));
            await Task.WhenAny(Task.WhenAll(jobs), Task.Delay(ms, ct));

            // 인간 마피아가 직접 사냥하는 밤에는 자동 랜덤 살해를 하지 않는다 — 못 죽였으면 '조용한 밤'.
            bool spatialHumanMafia = mafia != null && mafia.IsHuman && config.SpatialNightHunt;
            if (mafia != null && !spatialHumanMafia && string.IsNullOrEmpty(State.Night.MafiaTarget)) State.Night.MafiaTarget = RandomNightTarget(mafia);
            if (police != null && string.IsNullOrEmpty(State.Night.PoliceTarget)) State.Night.PoliceTarget = RandomNightTarget(police);
            if (doctor != null && string.IsNullOrEmpty(State.Night.DoctorTarget)) State.Night.DoctorTarget = RandomNightTarget(doctor);
        }

        async Task GatherNightInto(Player p, Action<string> assign, float nightEnd, CancellationToken ct)
        {
            var task = _actors[p.Id].NightAsync(State, p, ct);
            int ms = Mathf.Max(0, (int)((nightEnd - Time.realtimeSinceStartup) * 1000));
            var finished = await Task.WhenAny(task, Task.Delay(ms, ct));
            if (finished != task)
            {
                if (p.IsHuman && _actors[p.Id] is HumanActor ha)
                {
                    // 공간 사냥 마피아는 시간초과 시 랜덤 살해 없이 넘어감(놓친 밤). 그 외(경찰/의사)는 기존대로 랜덤.
                    bool spatialMafia = p.Role == Role.Mafia && config.SpatialNightHunt;
                    ha.ForceResolveChoice(spatialMafia ? null : RandomNightTarget(p));
                }
                else return;
            }

            var choice = await task;
            if (!string.IsNullOrEmpty(choice.TargetId)) assign(choice.TargetId);
        }

        string RandomNightTarget(Player p)
        {
            var valid = NightCandidates(p);
            return valid.Count == 0 ? null : valid[_rng.Next(valid.Count)];
        }

        List<string> NightCandidates(Player p)
        {
            switch (p.Role)
            {
                case Role.Mafia: return State.Alive.Where(x => x.Role != Role.Mafia).Select(x => x.Id).ToList();
                case Role.Doctor: return State.Alive.Select(x => x.Id).ToList();
                default: return State.Alive.Where(x => x.Id != p.Id).Select(x => x.Id).ToList();
            }
        }

        void DawnPhase(NightResult nr)
        {
            SetPhase(Phase.Dawn);
            if (!string.IsNullOrEmpty(nr.KilledId))
            {
                Emit(LogKind.Death, "SYSTEM", nr.KilledId + " 이(가) 밤사이 살해되었습니다.");
                var victim = State.ById(nr.KilledId);
                if (config.RevealRolesOnDeath && victim != null)
                    Emit(LogKind.Reveal, "SYSTEM", victim.Id + " 의 정체는 '" + victim.Role.Korean() + "'였다.");
            }
            else Emit(LogKind.System, "SYSTEM", "조용한 밤이었다. 아무도 죽지 않았다.");
        }

        async Task DiscussPhase(CancellationToken ct)
        {
            SetPhase(Phase.Discuss);
            Emit(LogKind.System, "SYSTEM", "── Day " + State.Day + " · 낮 토론 (" + config.DiscussionSeconds + "초) ──");
            _humanMsgs.Clear();

            float end = Time.realtimeSinceStartup + config.DiscussionSeconds;
            PhaseEndsAt = end;
            float nextMove = Time.realtimeSinceStartup + 1.5f;

            while (Time.realtimeSinceStartup < end)
            {
                ct.ThrowIfCancellationRequested();
                await DrainHumanMessages(ct);

                if (Time.realtimeSinceStartup >= nextMove)
                {
                    MoveOneNpc();
                    nextMove = Time.realtimeSinceStartup + Mathf.Max(2f, config.MoveIntervalMs / 1000f);
                }

                await RunOneRoomExchange(end, ct);

                await TalkDelay(end, ct);
            }
        }

        async Task DrainHumanMessages(CancellationToken ct)
        {
            while (_humanMsgs.Count > 0)
            {
                var m = _humanMsgs.Dequeue();
                string room = GetPlayerRoom(HumanPlayer.Id);
                EmitRoomSpeech(room, HumanPlayer.Id, m.Target, m.Text);

                // 플레이어가 특정 인물을 콕 집어 말을 걸었다면, 그 AI가 뒷순서를 기다리지 않고 바로 반응한다.
                if (m.Explicit)
                {
                    var pressed = State.ById(m.Target);
                    if (pressed != null && pressed.Alive && !pressed.IsHuman && GetPlayerRoom(pressed.Id) == room)
                    {
                        string reply = OneSentence(await _actors[pressed.Id].RebuttalAsync(State, pressed, HumanPlayer.Id, m.Text, ct));
                        EmitRoomSpeech(room, pressed.Id, HumanPlayer.Id, reply);
                    }
                }
            }
        }

        async Task RunOneRoomExchange(float end, CancellationToken ct)
        {
            var rooms = Rooms.Select(r => new { Room = r, People = State.Alive.Where(p => GetPlayerRoom(p.Id) == r).ToList() })
                             .Where(x => x.People.Count >= 2 && x.People.Any(p => !p.IsHuman))
                             .ToList();
            if (rooms.Count == 0) return;

            var pick = rooms[_rng.Next(rooms.Count)];
            var speakers = pick.People.Where(p => !p.IsHuman).ToList();
            var speaker = speakers[_rng.Next(speakers.Count)];
            var targets = pick.People.Where(p => p.Id != speaker.Id).ToList();
            var target = targets[_rng.Next(targets.Count)];
            string local = LocalTranscript(pick.Room);

            string question = OneSentence(await _actors[speaker.Id].AskRoomQuestionAsync(State, speaker, target, pick.Room, local, ct));
            EmitRoomSpeech(pick.Room, speaker.Id, target.Id, question);
            await TalkDelay(end, ct);

            if (target.IsHuman)
            {
                OnPlayerQuestion?.Invoke(speaker.Id, pick.Room, question);
                string answer = await WaitForHumanAnswer(speaker.Id, pick.Room, end, ct);
                if (!string.IsNullOrEmpty(answer) && answer != "...")
                {
                    EmitRoomSpeech(pick.Room, target.Id, speaker.Id, answer);
                    await TalkDelay(end, ct);
                    string reaction = OneSentence(await _actors[speaker.Id].ReactAsync(State, speaker, target.Id, answer, ct));
                    EmitRoomSpeech(pick.Room, speaker.Id, target.Id, reaction);
                }
                return;
            }

            string reply = OneSentence(await _actors[target.Id].AnswerRoomQuestionAsync(State, target, speaker, question, pick.Room, LocalTranscript(pick.Room), ct));
            EmitRoomSpeech(pick.Room, target.Id, speaker.Id, reply);
        }

        async Task TalkDelay(float end, CancellationToken ct)
        {
            int gap = _rng.Next(Mathf.Max(1, config.TalkIntervalMaxMs - config.TalkIntervalMinMs + 1)) + config.TalkIntervalMinMs;
            int remain = Mathf.Max(0, (int)((end - Time.realtimeSinceStartup) * 1000));
            if (remain > 0) await Task.Delay(Mathf.Min(gap, remain), ct);
        }

        async Task<string> WaitForHumanAnswer(string askerId, string room, float end, CancellationToken ct)
        {
            float answerEnd = Mathf.Min(end, Time.realtimeSinceStartup + 15f);
            while (Time.realtimeSinceStartup < answerEnd)
            {
                ct.ThrowIfCancellationRequested();
                if (_humanMsgs.Count > 0)
                {
                    var msg = _humanMsgs.Dequeue();
                    return OneSentence(msg.Text);
                }
                await Task.Delay(150, ct);
            }
            return null;
        }

        void MoveOneNpc()
        {
            var movers = State.Alive.Where(p => !p.IsHuman).ToList();
            if (movers.Count == 0) return;
            var p = movers[_rng.Next(movers.Count)];
            var next = AdjacentRooms(GetPlayerRoom(p.Id));
            if (next.Count == 0) return;
            string room = next[_rng.Next(next.Count)];
            _locations[p.Id] = room;
            Emit(LogKind.System, "SYSTEM", p.Id + " 이(가) " + room + "으로 이동했다.");
            OnLocationsChanged?.Invoke();
        }

        List<string> AdjacentRooms(string room)
        {
            return _mansion.Adjacency.TryGetValue(room, out var neighbors) ? neighbors : Rooms.ToList();
        }

        void EmitRoomSpeech(string room, string speaker, string target, string text)
        {
            text = OneSentence(text);
            _roomLines.Add(new RoomLine { Day = State.Day, Room = room, Speaker = speaker, Target = target, Text = text });
            string arrow = string.IsNullOrEmpty(target) ? "" : " → " + target;
            Emit(LogKind.Speech, speaker, "[" + room + "] " + speaker + arrow + ": " + text);
        }

        string LocalTranscript(string room)
        {
            var lines = _roomLines.Where(l => l.Room == room && l.Day == State.Day).Skip(Mathf.Max(0, _roomLines.Count - 10)).ToList();
            if (lines.Count == 0) return "";
            return string.Join("\n", lines.Select(l => "  " + l.Speaker + (string.IsNullOrEmpty(l.Target) ? "" : "→" + l.Target) + ": " + l.Text));
        }

        string OneSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "...";
            text = text.Replace("\n", " ").Replace("\r", " ").Trim();
            if (text == "...") return "...";
            char[] stops = { '.', '!', '?', '。', '！', '？' };
            int cut = text.IndexOfAny(stops);
            if (cut >= 0 && cut + 1 < text.Length) text = text.Substring(0, cut + 1);
            if (text.Length > 90) text = text.Substring(0, 90).Trim() + "...";
            return text;
        }

        async Task VotePhase(CancellationToken ct)
        {
            SetPhase(Phase.Vote);
            Emit(LogKind.System, "SYSTEM", "── Day " + State.Day + " · 투표 ──");
            State.Votes.Clear();

            foreach (var pl in State.AliveList)
            {
                ct.ThrowIfCancellationRequested();
                var choice = await _actors[pl.Id].VoteAsync(State, pl, ct);
                State.Votes[pl.Id] = choice.TargetId;
                Emit(LogKind.Vote, pl.Id, pl.Id + " ▶ " + choice.TargetId + " (" + choice.Reason + ")");
            }

            var vr = GameRules.ResolveVotes(State, _rng);
            var ex = State.ById(vr.ExecutedId);
            if (ex != null)
            {
                string tie = vr.Tie ? " (동수 추첨)" : "";
                Emit(LogKind.Reveal, "SYSTEM", ex.Id + " 이(가) 처형되었다. 정체는 '" + ex.Role.Korean() + "'!" + tie);
            }
            else Emit(LogKind.System, "SYSTEM", "표가 모이지 않아 처형이 무산되었다.");
        }

        void SetPhase(Phase p)
        {
            State.Phase = p;
            PhaseEndsAt = 0f;   // 타이머 있는 페이즈(밤/낮)가 진입 직후 다시 설정한다.
            OnPhaseChanged?.Invoke(State);
        }

        void Emit(LogKind kind, string speaker, string text)
        {
            State.Log(kind, speaker, text);
            var entry = State.PublicLog[State.PublicLog.Count - 1];
            if (logToConsole)
            {
                string who = kind == LogKind.Speech ? speaker : "◆";
                Debug.Log(who + " " + text);
            }
            OnLog?.Invoke(entry);
        }
    }
}
