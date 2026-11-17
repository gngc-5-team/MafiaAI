using System.Collections.Generic;

namespace MafiaAI.Alibi
{
    /// <summary>저택의 방 하나. 지도 격자 좌표, 원래 놓여 있던 흉기, 남몰래 할 법한 일(비밀 거짓말 재료).</summary>
    public sealed class RoomInfo
    {
        public readonly string Name;
        public readonly int X, Y;       // 지도 격자(0~2). 씬 빌더가 이 좌표로 방 노드를 저작한다
        public readonly string Weapon;  // 이 방에 원래 있던 흉기
        public readonly string Secret;  // 레드헤링이 이 방에서 숨기고 싶은 일(살인과 무관)

        public RoomInfo(string name, int x, int y, string weapon, string secret)
        {
            Name = name; X = x; Y = y; Weapon = weapon; Secret = secret;
        }
    }

    /// <summary>
    /// 고정 저택 지도. 랜덤 맵은 쓰지 않는다 — 플레이어가 머릿속에 지도를 들고 거리를 계산할 수 있어야 한다.
    /// 규칙: 15분(시간 한 칸)에 복도 한 칸까지만 이동할 수 있다.
    /// </summary>
    public static class AlibiMap
    {
        public static readonly RoomInfo[] Rooms =
        {
            new RoomInfo("서재",   0, 0, "청동 문진",   "피해자의 금고 번호를 알아내려고 책상 서랍을 뒤졌다"),
            new RoomInfo("응접실", 1, 0, "벽난로 부지깽이", "피해자의 서류 가방을 몰래 열어 봤다"),
            new RoomInfo("식당",   2, 0, "은촛대",     "은수저 몇 벌을 몰래 주머니에 챙겼다"),
            new RoomInfo("온실",   0, 1, "전지가위",   "피해자가 질색하는 담배를 숨어서 피웠다"),
            new RoomInfo("현관홀", 1, 1, "지팡이",     "몰래 저택을 빠져나가려고 현관 열쇠를 찾았다"),
            new RoomInfo("부엌",   2, 1, "식칼",       "저장고의 비싼 와인을 몰래 따서 마셨다"),
            new RoomInfo("세탁실", 1, 2, "다리미",     "피해자 외투 주머니에서 편지를 훔쳤다"),
            new RoomInfo("창고",   2, 2, "망치",       "창고에 숨겨 둔 도박 빚 문서를 없애려 했다"),
        };

        /// <summary>복도(무방향). 전부 격자상 이웃끼리만 잇는다(지도 그리기 단순화).</summary>
        public static readonly (string A, string B)[] Corridors =
        {
            ("서재", "응접실"), ("응접실", "식당"), ("응접실", "현관홀"),
            ("온실", "현관홀"), ("현관홀", "부엌"), ("식당", "부엌"),
            ("현관홀", "세탁실"), ("세탁실", "창고"), ("부엌", "창고"),
        };

        static readonly Dictionary<string, RoomInfo> _byName = new Dictionary<string, RoomInfo>();
        static readonly Dictionary<string, List<string>> _adj = new Dictionary<string, List<string>>();
        static readonly Dictionary<(string, string), int> _dist = new Dictionary<(string, string), int>();

        static AlibiMap()
        {
            foreach (var r in Rooms)
            {
                _byName[r.Name] = r;
                _adj[r.Name] = new List<string>();
            }
            foreach (var (a, b) in Corridors)
            {
                _adj[a].Add(b);
                _adj[b].Add(a);
            }
            foreach (var r in Rooms) Bfs(r.Name);
        }

        static void Bfs(string from)
        {
            var q = new Queue<string>();
            var seen = new Dictionary<string, int> { [from] = 0 };
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var n in _adj[cur])
                {
                    if (seen.ContainsKey(n)) continue;
                    seen[n] = seen[cur] + 1;
                    q.Enqueue(n);
                }
            }
            foreach (var kv in seen) _dist[(from, kv.Key)] = kv.Value;
        }

        public static RoomInfo Get(string name) => name != null && _byName.TryGetValue(name, out var r) ? r : null;
        public static bool IsRoom(string name) => name != null && _byName.ContainsKey(name);
        public static IReadOnlyList<string> Neighbors(string room) => _adj[room];

        /// <summary>복도 칸 수(최단). 같은 방이면 0.</summary>
        public static int Distance(string a, string b) => _dist[(a, b)];

        /// <summary>a에서 b까지 최단 경로(양 끝 포함).</summary>
        public static List<string> ShortestPath(string a, string b)
        {
            var path = new List<string> { a };
            var cur = a;
            while (cur != b)
            {
                foreach (var n in _adj[cur])
                {
                    if (Distance(n, b) == Distance(cur, b) - 1) { cur = n; break; }
                }
                path.Add(cur);
            }
            return path;
        }

        /// <summary>제자리 + 이웃 방(한 칸 시간 동안 갈 수 있는 곳).</summary>
        public static List<string> StayOrMove(string room)
        {
            var list = new List<string> { room };
            list.AddRange(_adj[room]);
            return list;
        }
    }
}
