using System;
using System.Collections.Generic;

namespace MafiaAI.Alibi
{
    /// <summary>보드에 드러난 진술 하나(아직 모르는 부분은 비어 있다).</summary>
    public struct StatementView
    {
        public string Person;
        public int Slot;
        public string Room;                 // null = 위치 미확인
        public bool SeenKnown;              // 누구를 봤는지까지 들었나
        public IReadOnlyList<string> Seen;

        public bool LocationKnown => Room != null;
    }

    public enum ContradictionKind { None, Travel, Presence, Witness, WitnessTravel }

    public sealed class Verdict
    {
        public bool Valid;
        public ContradictionKind Kind;
        public string Explanation;  // 성립했을 때: 왜 둘이 양립할 수 없는가
        public string Hint;         // 성립하지 않을 때: 왜 아닌가(규칙을 가르치는 문장)
    }

    /// <summary>
    /// 모순 판정은 LLM이 아니라 이 규칙이 한다(2026-08-01 교훈: 판정은 코드가).
    /// 규칙 3개뿐 — ① 15분에 복도 한 칸 ② 같은 시각 같은 방이면 서로 본다 ③ 봤다는 말은 그 사람의 위치를 증언한다.
    /// </summary>
    public static class ContradictionRules
    {
        public const int MinutesPerSlot = 15;

        public static Verdict Check(StatementView a, StatementView b)
        {
            if (a.Person == b.Person && a.Slot == b.Slot)
                return No("같은 진술을 두 번 골랐다.");
            if (!a.LocationKnown || !b.LocationKnown)
                return No("아직 드러나지 않은 진술이 있다. 먼저 물어봐야 한다.");

            // ① 같은 사람의 두 시각 — 이동 거리 대 시간
            if (a.Person == b.Person)
            {
                int d = AlibiMap.Distance(a.Room, b.Room);
                int dt = Math.Abs(a.Slot - b.Slot);
                if (d > dt)
                    return Yes(ContradictionKind.Travel,
                        $"{Josa.EunNeun(a.Person)} {T(a)}에 {a.Room}, {T(b)}에 {b.Room}에 있었다고 했다. " +
                        $"두 방은 복도 {d}칸 거리라 {dt * MinutesPerSlot}분 안에는 갈 수 없다.");
                return No($"{Josa.UiOf(a.Person)} 두 진술은 이어진다. 복도 {d}칸, 시간 {dt * MinutesPerSlot}분이면 충분히 오갈 수 있다.");
            }

            // ② 다른 사람, 같은 시각
            if (a.Slot == b.Slot)
            {
                if (a.Room == b.Room)
                {
                    var v = MissedEachOther(a, b) ?? MissedEachOther(b, a);
                    if (v != null) return v;
                    if (!a.SeenKnown && !b.SeenKnown)
                        return No($"둘 다 {T(a)}에 {a.Room}에 있었다고 한다. 같은 방이었다면 서로 봤어야 한다 — 누구를 봤는지 물어보면 드러난다.");
                    return No("서로 봤다는 진술이라 맞아떨어진다.");
                }
                var w = SawElsewhere(a, b) ?? SawElsewhere(b, a);
                if (w != null) return w;
                return No("각자 다른 방에 있었다는 말일 뿐이라 부딪히지 않는다. 같은 방이었다는 진술이나, 누군가를 봤다는 진술이 있어야 한다.");
            }

            // ③ 다른 사람, 다른 시각 — '봤다'는 증언이 그 사람의 위치를 고정한다
            var x = SawThenTravel(a, b) ?? SawThenTravel(b, a);
            if (x != null) return x;
            return No("다른 사람의 다른 시각 진술이라 직접 부딪히지 않는다. 연결 고리는 '누가 누구를 봤는가'다.");
        }

        // a는 그 방에서 b를 못 봤다는데 b도 그 방에 있었다고 한다
        static Verdict MissedEachOther(StatementView a, StatementView b)
        {
            if (!a.SeenKnown || Contains(a.Seen, b.Person)) return null;
            return Yes(ContradictionKind.Presence,
                $"{Josa.EunNeun(a.Person)} {T(a)}에 {Josa.EseoAt(a.Room)} {Josa.EulReul(b.Person)} 보지 못했다고 했다. " +
                $"그런데 {Josa.EunNeun(b.Person)} 그 시각 {a.Room}에 있었다고 한다. 같은 방이었다면 서로 봤어야 한다.");
        }

        // a는 자기 방에서 b를 봤다는데 b는 같은 시각 다른 방에 있었다고 한다
        static Verdict SawElsewhere(StatementView a, StatementView b)
        {
            if (!a.SeenKnown || !Contains(a.Seen, b.Person)) return null;
            return Yes(ContradictionKind.Witness,
                $"{Josa.EunNeun(a.Person)} {T(a)}에 {Josa.EseoAt(a.Room)} {Josa.EulReul(b.Person)} 봤다고 했다. " +
                $"그런데 {Josa.EunNeun(b.Person)} 그 시각 {b.Room}에 있었다고 한다.");
        }

        // a가 a.Slot에 a.Room에서 b를 봤다 → b는 그때 a.Room에 있었다. b의 다른 시각 진술과 거리가 맞는가
        static Verdict SawThenTravel(StatementView a, StatementView b)
        {
            if (!a.SeenKnown || !Contains(a.Seen, b.Person)) return null;
            int d = AlibiMap.Distance(a.Room, b.Room);
            int dt = Math.Abs(a.Slot - b.Slot);
            if (d <= dt) return null;
            return Yes(ContradictionKind.WitnessTravel,
                $"{Josa.EunNeun(a.Person)} {T(a)}에 {Josa.EseoAt(a.Room)} {Josa.EulReul(b.Person)} 봤다고 했다. " +
                $"그런데 {Josa.EunNeun(b.Person)} {T(b)}에 {b.Room}에 있었다고 한다. 복도 {d}칸이라 {dt * MinutesPerSlot}분 안에는 갈 수 없다.");
        }

        /// <summary>진술 묶음 안의 모든 모순 쌍(생성기 검증용).</summary>
        public static List<(StatementView A, StatementView B, Verdict V)> FindAll(IList<StatementView> all)
        {
            var list = new List<(StatementView, StatementView, Verdict)>();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    var v = Check(all[i], all[j]);
                    if (v.Valid) list.Add((all[i], all[j], v));
                }
            return list;
        }

        static bool Contains(IReadOnlyList<string> list, string name)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] == name) return true;
            return false;
        }

        static string T(StatementView s) => AlibiCase.TimeLabel(s.Slot);
        static Verdict Yes(ContradictionKind k, string why) => new Verdict { Valid = true, Kind = k, Explanation = why };
        static Verdict No(string hint) => new Verdict { Valid = false, Kind = ContradictionKind.None, Hint = hint };
    }

    /// <summary>한국어 조사(받침 유무로 고른다). 이름·방 이름이 동적이라 하드코딩하면 틀린다.</summary>
    public static class Josa
    {
        static bool HasBatchim(string word, out bool rieul)
        {
            rieul = false;
            if (string.IsNullOrEmpty(word)) return false;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return false;
            int jong = (c - 0xAC00) % 28;
            rieul = jong == 8;
            return jong != 0;
        }

        public static string EunNeun(string w) => w + (HasBatchim(w, out _) ? "은" : "는");
        public static string IGa(string w) => w + (HasBatchim(w, out _) ? "이" : "가");
        public static string EulReul(string w) => w + (HasBatchim(w, out _) ? "을" : "를");
        public static string WaGwa(string w) => w + (HasBatchim(w, out _) ? "과" : "와");
        public static string UiOf(string w) => w + "의";
        public static string EseoAt(string w) => w + "에서";
        public static string Ro(string w)
        {
            bool b = HasBatchim(w, out bool rieul);
            return w + (b && !rieul ? "으로" : "로");
        }
    }
}
