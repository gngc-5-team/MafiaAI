using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Core
{
    /// <summary>
    /// 던전 생성에서 흔히 쓰는 방식으로 저택의 방 그래프와 실제 배치를 매판 랜덤 생성한다.
    /// 1) 무작위 신장 트리로 모든 방이 서로 이어지도록 보장한다(고립된 방 없음).
    /// 2) 그 신장 트리를 4방향 격자에 그대로 얹어 각 방의 그리드 좌표를 정한다 —
    ///    트리 간선은 항상 격자 상 이웃이 되므로 물리 복도(직선/회전만 있으면 됨)로 그대로 그릴 수 있다.
    /// 3) 일직선 외길이 되지 않도록, 물리 복도는 없지만 AI 동선 판단에는 지름길로 쓰이는 여분 간선을 논리 그래프에 더 붙인다.
    /// UnityEngine에 의존하지 않는 순수 C#이라 그대로 테스트하고 GameController에 꽂아 쓴다.
    /// </summary>
    public static class MansionGenerator
    {
        public readonly struct Layout
        {
            /// <summary>AI가 다음에 갈 방을 고를 때 쓰는 논리 그래프(여분 간선 포함).</summary>
            public readonly Dictionary<string, List<string>> Adjacency;
            /// <summary>방 하나당 격자 좌표(칸 단위). 렌더러가 셀 크기를 곱해 월드 좌표로 바꾼다.</summary>
            public readonly Dictionary<string, (int X, int Y)> GridPos;
            /// <summary>실제로 복도를 그려야 하는 방 쌍(신장 트리 간선, 항상 격자 상 이웃).</summary>
            public readonly List<(string A, string B)> Corridors;

            public Layout(Dictionary<string, List<string>> adjacency, Dictionary<string, (int, int)> gridPos, List<(string, string)> corridors)
            {
                Adjacency = adjacency;
                GridPos = gridPos;
                Corridors = corridors;
            }
        }

        static readonly (int dx, int dy)[] Dirs4 = { (0, 1), (0, -1), (1, 0), (-1, 0) };

        public static Layout Generate(IReadOnlyList<string> rooms, IRng rng, int extraEdges = 2, int maxDegree = 3)
        {
            var adjacency = rooms.ToDictionary(r => r, r => new List<string>());
            var gridPos = new Dictionary<string, (int X, int Y)>();
            var corridors = new List<(string A, string B)>();
            if (rooms.Count == 0) return new Layout(adjacency, gridPos, corridors);

            // 1)+2) 신장 트리를 만들면서 동시에 격자에 앉힌다: 새 방은 항상 이미 놓인 방의 빈 사방 칸 중 하나에 놓는다.
            var unplaced = Shuffle(rooms.ToList(), rng);
            string root = unplaced[0];
            unplaced.RemoveAt(0);
            gridPos[root] = (0, 0);
            var placed = new List<string> { root };

            while (unplaced.Count > 0)
            {
                string anchor = Shuffle(placed.ToList(), rng).FirstOrDefault(p => FreeNeighbors(p, gridPos).Count > 0);
                if (anchor == null) break; // 이 규모의 저택에선 이론상 발생하지 않는 방어 코드

                var cell = Shuffle(FreeNeighbors(anchor, gridPos), rng)[0];
                string next = unplaced[rng.Next(unplaced.Count)];
                unplaced.Remove(next);

                gridPos[next] = cell;
                Connect(adjacency, next, anchor);
                corridors.Add((next, anchor));
                placed.Add(next);
            }

            // 3) 루프용 여분 간선: 물리 복도는 없지만 AI 동선 판단엔 지름길로 반영한다.
            var pairs = Shuffle(AllPairs(rooms), rng);
            int added = 0;
            foreach (var pair in pairs)
            {
                if (added >= extraEdges) break;
                string a = pair[0], b = pair[1];
                if (adjacency[a].Contains(b)) continue;
                if (adjacency[a].Count >= maxDegree || adjacency[b].Count >= maxDegree) continue;
                Connect(adjacency, a, b);
                added++;
            }

            return new Layout(adjacency, gridPos, corridors);
        }

        static List<(int, int)> FreeNeighbors(string room, Dictionary<string, (int X, int Y)> gridPos)
        {
            var (x, y) = gridPos[room];
            var occupied = new HashSet<(int, int)>(gridPos.Values);
            return Dirs4.Select(d => (x + d.dx, y + d.dy)).Where(c => !occupied.Contains(c)).ToList();
        }

        static void Connect(Dictionary<string, List<string>> adjacency, string a, string b)
        {
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        static List<string[]> AllPairs(IReadOnlyList<string> rooms)
        {
            var pairs = new List<string[]>();
            for (int i = 0; i < rooms.Count; i++)
                for (int j = i + 1; j < rooms.Count; j++)
                    pairs.Add(new[] { rooms[i], rooms[j] });
            return pairs;
        }

        static List<T> Shuffle<T>(List<T> list, IRng rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
            return list;
        }
    }
}
