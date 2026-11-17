using System;
using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Mystery
{
    // ─────────────────────────────────────────────────────────────────────────────
    // 직접 쓴 사건 한 편을 담는 데이터. 진실·거짓말·단서의 연결은 전부 여기 있고,
    // 세션(MysterySession)은 이 표를 따라 상태만 바꾼다. LLM은 이 데이터를 바꾸지 못한다.
    //
    // 플래그 규칙(조건식에서 쓴다):
    //   has:증거   seen:조사지점   detail:단서   heard:진술   broken:진술   asked:질문쪽지
    //   met:인물   dead:인물      그 밖에 사건이 직접 켜는 이름(ch6_burned 등)
    // 조건식: "a,b" = a 그리고 b,  "a|b" = a 또는 b(쉼표보다 먼저 묶인다),  "!a" = a가 아님.
    // ─────────────────────────────────────────────────────────────────────────────

    public enum EvidenceKind { Physical, Testimony, Document }

    /// <summary>재구성 빈칸에 들어가는 단어의 종류.</summary>
    public enum WordKind { Person, Time, Object, Deception, Place, Secret }

    /// <summary>타임라인 한 줄(시각은 분: 22:30 = 1350, 자정 넘으면 1440 이상).</summary>
    public sealed class TimeNote
    {
        public int At;
        public string Text;
        public TimeNote(int at, string text) { At = at; Text = text; }
    }

    /// <summary>증거 그림 속 숨은 단서 — 확대해서 그 자리를 눌러야 드러난다.</summary>
    public sealed class Detail
    {
        public string Id;
        public string Label;          // 찾은 뒤 목록에 보일 이름
        public string Text;           // 찾았을 때 서술
        public float X, Y, W, H;      // 증거 그림 안 정규화 좌표(왼쪽 위 0,0 / 오른쪽 아래 1,1)
        public string[] Words = Array.Empty<string>();
        public TimeNote[] Timeline = Array.Empty<TimeNote>();
    }

    public sealed class Evidence
    {
        public string Id;
        public string Name;
        public EvidenceKind Kind;
        public string Image;          // Art/Mystery/Evidence/{Image}.png (없으면 종류별 기본 그림)
        public string Source;         // 어디서 얻었나
        public string Summary;        // 목록 한 줄
        public string Text;           // 자세한 설명
        public string Document;       // 읽을 거리(원고·쪽지 본문) — 있으면 '읽기'가 생긴다
        public int Chapter;           // 원고라면 장 번호(원고 탭 정렬)
        public Detail[] Details = Array.Empty<Detail>();
        public string[] Words = Array.Empty<string>();
        public TimeNote[] Timeline = Array.Empty<TimeNote>();
    }

    public sealed class Hotspot
    {
        public string Id;
        public string Label;          // 마우스를 올리면 뜨는 이름
        public float X, Y, W, H;      // 방 그림 안 정규화 좌표
        public string Text;           // 살펴봤을 때 서술
        public string Gives;          // 얻는 증거(없으면 서술만)
        public string ShowIf;         // 이 조건일 때만 보인다
        public string[] SetFlags = Array.Empty<string>();
        public string[] Words = Array.Empty<string>();
    }

    /// <summary>조건에 따라 방 그림 위에 얹는 그림(두 번째 살인 뒤 온실의 흰 천 등).</summary>
    public sealed class Overlay
    {
        public string Image;
        public float X, Y, W, H;
        public string ShowIf;
    }

    public sealed class Room
    {
        public string Id;
        public string Name;
        public string Image;          // Art/Mystery/Rooms/{Image}.png
        public string Intro;          // 들어설 때 한 줄
        public int X, Y;              // 도면 칸
        public Hotspot[] Hotspots = Array.Empty<Hotspot>();
        public Overlay[] Overlays = Array.Empty<Overlay>();
    }

    public sealed class Statement
    {
        public string Id;
        public string Text;
        public bool Lie;
        public string ShowIf;         // 이 진술이 목록에 오르는 조건(없으면 첫 대면부터)
        public string HideIf;         // 깨진 뒤 다른 진술로 바뀌는 경우
        public string[] BrokenBy = Array.Empty<string>();   // "증거" 또는 "증거@플래그"(숨은 단서까지 찾아야 함)
        public string[] Related = Array.Empty<string>();    // 방향은 맞지만 결정적이지 않은 증거(덜 깎인다)
        public string NearMiss;       // 증거는 맞는데 결정적 단서를 아직 못 찾았을 때의 대꾸
        public string Why;            // 「모순!」 컷신 아래 한 줄 설명
        public string[] BreakLines = Array.Empty<string>();
        public string[] Gives = Array.Empty<string>();
        public string[] SetFlags = Array.Empty<string>();
        public string[] Words = Array.Empty<string>();
        public string Hears;          // 이 진술을 들으면 얻는 증언(사실인 진술)
        public string Truth;          // 깨진 뒤 이 사람이 인정한 사실(자유 질문 LLM에 준다)
        public string Hint;           // 깨지기 전: 이 거짓을 지키는 동안의 태도·넌지시 가리키는 곳(내용은 담지 않는다 — 자유 질문 LLM에 준다)
        public string[] Guard = Array.Empty<string>();   // 깨지기 전 자유 질문 답에 나오면 안 되는 말(새어 나가면 버린다)
    }

    public sealed class Topic
    {
        public string Id;
        public string Label;          // 질문 쪽지
        public string[] Lines = Array.Empty<string>();
        public string ShowIf;
        public string HideIf;
        public string[] Gives = Array.Empty<string>();
        public string[] SetFlags = Array.Empty<string>();
        public string[] Words = Array.Empty<string>();
    }

    public sealed class Suspect
    {
        public string Name;
        public string Role;           // 직업(사진가·의사…)
        public string Bio;            // 심문 화면 한 줄 소개
        public string[] Greeting = Array.Empty<string>();
        public Statement[] Statements = Array.Empty<Statement>();
        public Topic[] Topics = Array.Empty<Topic>();
        public Dictionary<string, string> Reactions = new Dictionary<string, string>();  // 보여 주기: 증거 → 대사
        public string DefaultReaction;
        public string[] WrongLines = Array.Empty<string>();  // 엉뚱한 증거를 들이밀었을 때
        public string Facts;          // 자유 질문용: 이 사람이 처음부터 숨김없이 말하는 사실
        public string Persona;        // 자유 질문용: 성격·내력·노아와의 사이·다른 손님에 대한 인상·지금 기분(비밀은 담지 않는다)
        public string ClosedIf;       // 이 조건이면 자유 질문·질문 쪽지에 입을 닫는다
        public string ClosedLine;
    }

    public sealed class Question
    {
        public string Id;
        public string Text;
        public string Hint;
        public string OpenIf;
        public string ResolveIf;
        public string Answer;         // 풀린 뒤 한 줄
    }

    /// <summary>범인이 시간에 맞춰 움직이는 일(원고 태우기, 두 번째 살인 등).</summary>
    public sealed class TimedEvent
    {
        public string Id;
        public int At;
        public string Unless;         // 그 시각에 이 조건이면 일어나지 않는다(막아 냄)
        public string[] SetFlags = Array.Empty<string>();
        public string Kill;           // 죽는 사람
        public string Room;           // 일이 벌어진 방
        public string Toast;          // 짧은 알림
        public string PreventedToast; // 막아 냈을 때 알림
        public string CutTitle;       // 있으면 컷신(제목)
        public string CutSub;
        public string CutNote;
    }

    /// <summary>손님의 동선 한 점 — 시계가 At을 지나면 그 방의 X(0~1) 자리로 옮겨 간다.</summary>
    public sealed class Stop
    {
        public string Who;
        public int At;
        public string Room;
        public float X;
        public Stop(string who, int at, string room, float x) { Who = who; At = at; Room = room; X = x; }
    }

    public sealed class Word
    {
        public string Id;
        public string Label;
        public WordKind Kind;
        public string AlwaysIf;       // 처음부터 쓸 수 있는 단어면 "" (그 밖에는 얻어야 쓴다)
    }

    public sealed class Blank
    {
        public string Id;
        public WordKind Kind;
        public string Answer;         // 정답 단어 id
    }

    public sealed class Rebuttal
    {
        public string Line;           // 범인의 반박
        public string[] Accept = Array.Empty<string>();
        public string Counter;        // 탐정이 증거를 내밀 때 한 줄
        public string[] Success = Array.Empty<string>();
        public string Wrong;
    }

    public sealed class Panel
    {
        public string Image;          // Art/Mystery/Story/{Image}.png
        public string Text;
        public Panel(string image, string text) { Image = image; Text = text; }
    }

    public sealed class Ending
    {
        public string Id;
        public string Title;
        public bool Solved;
        public Panel[] Panels = Array.Empty<Panel>();
    }

    public sealed class MysteryCase
    {
        public string Title;
        public string Subtitle;
        public string Victim;
        public string Killer;
        public int Start;             // 수사 시작 시각(분)
        public int Dawn;              // 이 시각이 되면 경찰이 온다(끝)
        public Panel[] Prologue = Array.Empty<Panel>();
        public Room[] Rooms = Array.Empty<Room>();
        public string[][] Corridors = Array.Empty<string[]>();
        public string StartRoom;
        public Evidence[] Evidence = Array.Empty<Evidence>();
        public Suspect[] Suspects = Array.Empty<Suspect>();
        public Question[] Questions = Array.Empty<Question>();
        public TimedEvent[] Events = Array.Empty<TimedEvent>();
        public Word[] Words = Array.Empty<Word>();
        public Blank[] Blanks = Array.Empty<Blank>();
        public string[] Sentences = Array.Empty<string>();   // "{빈칸:조사}" 자리표시
        public string[] Rehearsal = Array.Empty<string>();   // 재구성 직전 범인의 한마디
        public Rebuttal[] Rebuttals = Array.Empty<Rebuttal>();
        public string[] Confession = Array.Empty<string>();   // "인물|대사"
        public Ending[] Endings = Array.Empty<Ending>();
        public string[] Truth = Array.Empty<string>();        // 사건 보고서의 실제 경위
        public Stop[] Schedule = Array.Empty<Stop>();         // 손님들의 동선(저택 단면판)

        // 행동 값(분)
        public int MoveCost = 5;            // (평면판) 복도 한 칸
        public int WalkMinutesPerRoom = 2;  // (단면판) 같은 층 옆방으로 한 칸 걷기
        public int StairMinutes = 3;        // (단면판) 계단 한 층
        public int ExamineCost = 3;
        public int FlavorExamineCost = 1;   // 증거가 안 나오는 곳 — 둘러보는 호기심은 거의 벌주지 않는다
        public int AskCost = 3;
        public int ShowCost = 2;
        public int PresentCost = 5;
        public int NearMissCost = 5;
        public int WrongCost = 15;
        public int FreeAskCost = 3;
        public int WrongTheoryCost = 30;
        public int WrongRebuttalCost = 10;
        public int Resolve = 3;       // 최후의 추궁에서 틀려도 되는 횟수

        // ── 조회 ──
        public Room RoomById(string id) => Rooms.First(r => r.Id == id);
        public Evidence Ev(string id) => Evidence.FirstOrDefault(e => e.Id == id);
        public Suspect SuspectByName(string name) => Suspects.FirstOrDefault(s => s.Name == name);
        public Word WordById(string id) => Words.FirstOrDefault(w => w.Id == id);

        public IEnumerable<(Suspect Who, Statement St)> AllStatements =>
            Suspects.SelectMany(s => s.Statements.Select(st => (s, st)));

        public static string Clock(int minutes)
        {
            int m = ((minutes % 1440) + 1440) % 1440;
            return $"{m / 60:00}:{m % 60:00}";
        }

        public static int At(int hour, int minute) => (hour < 12 ? hour + 24 : hour) * 60 + minute;

        /// <summary>단면도에서 층이 다른 이웃 방(계단)인가.</summary>
        public bool IsStair(string a, string b) => RoomById(a).X == RoomById(b).X && RoomById(a).Y != RoomById(b).Y;

        public bool Adjacent(string a, string b) => Corridors.Any(c => (c[0] == a && c[1] == b) || (c[0] == b && c[1] == a));

        public string FloorLabel(string roomId)
        {
            int y = RoomById(roomId).Y;
            return y == 0 ? "2F" : y == 1 ? "1F" : "B1";
        }

        /// <summary>걸어서 가는 데 드는 분(같은 층 한 칸 = WalkMinutesPerRoom, 계단 한 층 = StairMinutes) — 가장 빠른 길.</summary>
        public int TravelMinutes(string from, string to)
        {
            var path = Path(from, to);
            if (path == null) return 999;
            int m = 0;
            for (int i = 1; i < path.Count; i++) m += IsStair(path[i - 1], path[i]) ? StairMinutes : WalkMinutesPerRoom;
            return m;
        }

        /// <summary>가장 적게 걷는 길(방 id 순서, 출발 포함). 못 가면 null.</summary>
        public List<string> Path(string from, string to)
        {
            var dist = new Dictionary<string, int> { [from] = 0 };
            var prev = new Dictionary<string, string>();
            var open = new List<string> { from };
            while (open.Count > 0)
            {
                var cur = open.OrderBy(r => dist[r]).First();
                open.Remove(cur);
                if (cur == to) break;
                foreach (var c in Corridors)
                {
                    string next = c[0] == cur ? c[1] : c[1] == cur ? c[0] : null;
                    if (next == null) continue;
                    int d = dist[cur] + (IsStair(cur, next) ? StairMinutes : WalkMinutesPerRoom);
                    if (dist.TryGetValue(next, out var old) && old <= d) continue;
                    dist[next] = d;
                    prev[next] = cur;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            if (!dist.ContainsKey(to)) return null;
            var path = new List<string> { to };
            while (path[0] != from) path.Insert(0, prev[path[0]]);
            return path;
        }

        /// <summary>복도로 이어진 방 사이 걸음 수(BFS).</summary>
        public int Distance(string from, string to)
        {
            if (from == to) return 0;
            var dist = new Dictionary<string, int> { [from] = 0 };
            var q = new Queue<string>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var c in Corridors)
                {
                    string next = c[0] == cur ? c[1] : c[1] == cur ? c[0] : null;
                    if (next == null || dist.ContainsKey(next)) continue;
                    dist[next] = dist[cur] + 1;
                    if (next == to) return dist[next];
                    q.Enqueue(next);
                }
            }
            return 99;
        }
    }

    /// <summary>플래그 조건식.</summary>
    public static class Cond
    {
        public static bool Eval(string expr, Func<string, bool> has)
        {
            if (string.IsNullOrWhiteSpace(expr)) return true;
            foreach (var andPart in expr.Split(','))
            {
                bool any = false;
                foreach (var orPart in andPart.Split('|'))
                {
                    var t = orPart.Trim();
                    if (t.Length == 0) continue;
                    bool neg = t.StartsWith("!");
                    if (neg) t = t.Substring(1).Trim();
                    if (has(t) != neg) { any = true; break; }
                }
                if (!any) return false;
            }
            return true;
        }
    }
}
