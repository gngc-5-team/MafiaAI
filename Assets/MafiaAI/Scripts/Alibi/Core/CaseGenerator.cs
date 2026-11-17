using System;
using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Alibi
{
    /// <summary>
    /// 사건 생성기. ① 밤을 시뮬레이션해 진실(동선·목격)을 만들고 ② 범인과 레드헤링의 거짓말을 '데이터로' 심은 뒤
    /// ③ 모순 규칙으로 풀 수 있는 사건인지 검증한다. 검증을 통과하지 못하면 버리고 다시 만든다.
    ///
    /// 거짓말의 원칙: 거짓말쟁이는 '누가 자기를 봤는지' 안다(같은 방이면 서로 본다).
    /// 그래서 목격된 시각은 사실대로 말하고, 아무도 못 본 시각만 꾸민다 — 그 꾸민 방이 다른 사실과 부딪히게 설계한다.
    /// </summary>
    public static class CaseGenerator
    {
        public sealed class Options
        {
            public double ChainChance = 0.3;   // 범인 모순이 레드헤링 고백 뒤에만 드러나는 사건 비율
            public int MaxAttempts = 5000;
        }

        const int Tm = AlibiCase.MurderSlot;
        const int N = 4; // AlibiCase.SlotTimes.Length

        public static AlibiCase Generate(int seed, Options opt = null)
        {
            opt = opt ?? new Options();
            var rng = new Random(seed);
            bool wantChain = rng.NextDouble() < opt.ChainChance;
            for (int attempt = 0; attempt < opt.MaxAttempts; attempt++)
            {
                var c = TryBuild(rng, seed, wantChain && attempt < opt.MaxAttempts / 2);
                if (c != null) return c;
            }
            throw new InvalidOperationException("사건 생성 실패(seed " + seed + ")");
        }

        // ───────────────────────── 1. 진실 시뮬레이션 ─────────────────────────

        static AlibiCase TryBuild(Random rng, int seed, bool chain)
        {
            var names = CharacterSheets.Names.OrderBy(_ => rng.Next()).ToList();
            string victim = names[0];
            var suspects = names.Skip(1).Select(n => new Suspect { Name = n }).ToList();
            int killerIdx = rng.Next(suspects.Count);
            int secretIdx;
            do secretIdx = rng.Next(suspects.Count); while (secretIdx == killerIdx);
            var K = suspects[killerIdx];
            var S = suspects[secretIdx];
            K.IsKiller = true;
            S.IsSecretLiar = true;

            var rooms = AlibiMap.Rooms.Select(r => r.Name).ToList();
            string M = Pick(rng, rooms);

            // 흉기: 살해 시각까지 들고 올 수 있는 방에서. 대부분 다른 방 물건이다(흉기 출처가 동선 단서가 되게).
            var wCands = rooms.Where(r => AlibiMap.Distance(r, M) <= Tm).ToList();
            var wOther = wCands.Where(r => r != M).ToList();
            string W = (rng.NextDouble() < 0.85 && wOther.Count > 0) ? Pick(rng, wOther) : M;
            int tw = rng.Next(0, Tm - AlibiMap.Distance(W, M) + 1);
            if (W != M && tw > Tm - 1) tw = Tm - 1;

            // 범인 동선: tw에 흉기 방 → Tm에 살해 방 → 다음 칸엔 빠져나감
            var kp = new string[N];
            kp[tw] = W;
            for (int s = tw + 1; s <= Tm; s++)
            {
                int left = Tm - s;
                var opts = AlibiMap.StayOrMove(kp[s - 1]).Where(x => AlibiMap.Distance(x, M) <= left).ToList();
                kp[s] = Pick(rng, opts);
            }
            for (int s = tw - 1; s >= 0; s--) kp[s] = Pick(rng, AlibiMap.StayOrMove(kp[s + 1]));
            kp[Tm + 1] = Pick(rng, AlibiMap.Neighbors(M).ToList());
            K.TruePath = kp;

            // 피해자 동선: Tm에 살해 방(그 뒤는 시체)
            var vp = new string[N];
            vp[Tm] = M;
            for (int s = Tm - 1; s >= 0; s--) vp[s] = Pick(rng, AlibiMap.StayOrMove(vp[s + 1]));

            // 레드헤링의 비밀: ts에 비밀의 방 Q
            int ts;
            string Q;
            if (chain)
            {
                // 연쇄 사건: 범인이 지나간 방·시각에 레드헤링도 있었다 — 둘 다 그 시각을 숨긴다
                var cands = new List<int>();
                for (int s = 0; s < N; s++) if (s != Tm && kp[s] != M) cands.Add(s);
                if (cands.Count == 0) return null;
                ts = Pick(rng, cands);
                Q = kp[ts];
            }
            else
            {
                ts = rng.Next(N);
                Q = Pick(rng, rooms.Where(r => r != M).ToList());
            }

            // 무고한 사람 동선(레드헤링 포함)
            foreach (var p in suspects)
            {
                if (p == K) continue;
                var path = new string[N];
                if (p == S)
                {
                    path[ts] = Q;
                    for (int s = ts + 1; s < N; s++) path[s] = Wander(rng, path[s - 1]);
                    for (int s = ts - 1; s >= 0; s--) path[s] = Wander(rng, path[s + 1]);
                }
                else
                {
                    path[0] = Pick(rng, rooms);
                    for (int s = 1; s < N; s++) path[s] = Wander(rng, path[s - 1]);
                }
                // 살해 현장을 본 목격자, 시체를 먼저 발견한 사람은 없다(시체는 23:45에 하인이 발견)
                if (path[Tm] == M || path[Tm + 1] == M) return null;
                p.TruePath = path;
            }

            // 진실 목격: 같은 시각 같은 방이면 서로 본다(피해자는 살해 시각까지 살아 있다)
            foreach (var p in suspects)
            {
                p.TrueSeen = new List<string>[N];
                for (int s = 0; s < N; s++)
                {
                    var seen = suspects.Where(o => o != p && o.TruePath[s] == p.TruePath[s]).Select(o => o.Name).ToList();
                    if (s <= Tm && vp[s] == p.TruePath[s]) seen.Add(victim);
                    p.TrueSeen[s] = seen;
                }
                p.Claims = Enumerable.Range(0, N).Select(s => p.Truth(s)).ToArray();
            }

            // 연쇄 사건이면 그 시각 비밀의 방에는 범인과 레드헤링(그리고 말 못 하는 피해자)뿐이어야 한다
            if (chain)
            {
                var others = suspects.Where(o => o != K && o != S && o.TruePath[ts] == Q);
                if (others.Any()) return null;
            }

            var c = new AlibiCase
            {
                Seed = seed,
                Victim = victim,
                VictimPath = Enumerable.Range(0, N).Select(s => s <= Tm ? vp[s] : null).ToArray(),
                MurderRoom = M,
                Weapon = AlibiMap.Get(W).Weapon,
                WeaponRoom = W,
                WeaponSlot = tw,
                Suspects = suspects,
                ChainOnly = chain,
            };
            S.SecretRoom = Q;
            S.SecretSlot = ts;
            S.Secret = AlibiMap.Get(Q).Secret;

            // ───────────── 2. 거짓말 심기 ─────────────
            if (!PlantKillerLie(rng, c, chain)) return null;
            if (!PlantSecretLie(rng, c, chain)) return null;

            // ───────────── 3. 풀 수 있는 사건인지 검증 ─────────────
            if (!Verify(c)) return null;

            foreach (var p in suspects)
            {
                p.OriginalClaims = p.Claims.Select(x => x.Clone()).ToArray();
                p.Relation = CharacterSheets.RandomRelation(rng, victim);
            }
            return c;
        }

        static string Wander(Random rng, string from) =>
            rng.NextDouble() < 0.35 ? from : Pick(rng, AlibiMap.Neighbors(from).ToList());

        static T Pick<T>(Random rng, IList<T> list) => list[rng.Next(list.Count)];

        // ───────────────────────── 2. 거짓말 ─────────────────────────

        /// <summary>그 시각 그 방에 (피해자·범인 말고) 증언할 수 있는 사람이 있었나.</summary>
        static bool WitnessAt(AlibiCase c, string room, int slot, string except1 = null, string except2 = null) =>
            c.Suspects.Any(o => !o.IsKiller && o.Name != except1 && o.Name != except2 && o.TruePath[slot] == room);

        /// <summary>범인 본인을 본 산 사람(무고한 용의자)이 있었나.</summary>
        static bool KillerSeenAt(AlibiCase c, int slot)
        {
            var k = c.Killer;
            return c.Suspects.Any(o => !o.IsKiller && o.TruePath[slot] == k.TruePath[slot]);
        }

        static bool PlantKillerLie(Random rng, AlibiCase c, bool chain)
        {
            var k = c.Killer;
            var s = c.SecretLiar;
            var kinds = chain
                ? new List<LieKind> { LieKind.None } // 직접 모순이 없는 '깨끗한' 거짓말
                : new List<LieKind> { LieKind.Presence, LieKind.Travel, LieKind.Witness }.OrderBy(_ => rng.Next()).ToList();

            foreach (var kind in kinds)
            {
                var fixedSlots = new Dictionary<int, string>();   // 사실대로 말하는 시각
                var mustLie = new HashSet<int> { Tm };
                var flex = new HashSet<int>();                    // 이야기를 맞추려고 바꿀 수도 있는 시각
                if (chain) mustLie.Add(s.SecretSlot);

                for (int t = 0; t < N; t++)
                {
                    if (mustLie.Contains(t)) continue;
                    bool nearLie = mustLie.Any(m => Math.Abs(m - t) == 1);
                    bool seen = KillerSeenAt(c, t);
                    if (nearLie && !seen) flex.Add(t);
                    else fixedSlots[t] = k.TruePath[t];
                }

                if (kind == LieKind.Witness)
                {
                    // 목격된 직후/직전 시각까지 우긴다 — 증인이 있다는 걸 알면서 거는 도박
                    var cand = new[] { Tm - 1, Tm + 1 }.Where(t => t >= 0 && t < N && fixedSlots.ContainsKey(t) && KillerSeenAt(c, t)).ToList();
                    if (cand.Count == 0) continue;
                    int a = Pick(rng, cand);
                    fixedSlots.Remove(a);
                    mustLie.Add(a);
                }

                var choice = SolveClaims(rng, c, k, fixedSlots, mustLie, flex, kind);
                if (choice == null) continue;
                foreach (var kv in choice) k.Claims[kv.Key] = new Claim(kv.Value, Enumerable.Empty<string>());
                c.KillerLie = kind;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 거짓 진술 방 고르기 — 고정 시각은 사실, 꼭 거짓말할 시각은 진실과 다른 방, 유연한 시각은 진실 우선.
        /// 틀(kind)에 맞는 조합 중 거짓말 수가 가장 적은 것을 무작위로 고른다(최소한만 꾸미는 게 사람다운 거짓말).
        /// </summary>
        static Dictionary<int, string> SolveClaims(Random rng, AlibiCase c, Suspect who,
            Dictionary<int, string> fixedSlots, HashSet<int> mustLie, HashSet<int> flex, LieKind kind)
        {
            var free = mustLie.Concat(flex).OrderBy(x => x).ToList();
            var rooms = AlibiMap.Rooms.Select(r => r.Name).ToList();
            var best = new List<Dictionary<int, string>>();
            int bestLies = int.MaxValue;
            var cur = new Dictionary<int, string>();

            void Rec(int i)
            {
                if (i == free.Count)
                {
                    var claim = new string[N];
                    for (int t = 0; t < N; t++) claim[t] = fixedSlots.TryGetValue(t, out var f) ? f : cur[t];
                    if (!Acceptable(c, who, claim, fixedSlots, mustLie, kind)) return;
                    int lies = free.Count(t => claim[t] != who.TruePath[t]);
                    if (lies < bestLies) { bestLies = lies; best.Clear(); }
                    if (lies == bestLies) best.Add(free.ToDictionary(t => t, t => claim[t]));
                    return;
                }
                int slot = free[i];
                foreach (var r in rooms)
                {
                    if (mustLie.Contains(slot) && r == who.TruePath[slot]) continue;
                    if (who.IsKiller && slot == Tm && r == c.MurderRoom) continue;
                    cur[slot] = r;
                    Rec(i + 1);
                }
                cur.Remove(slot);
            }
            Rec(0);
            return best.Count == 0 ? null : best[rng.Next(best.Count)];
        }

        static bool Acceptable(AlibiCase c, Suspect who, string[] claim, Dictionary<int, string> fixedSlots, HashSet<int> mustLie, LieKind kind)
        {
            // 범인은 꾸며 내는 시각에 살해 방을 대지 않는다(누가 봐도 수상한 자백이 된다).
            // 목격돼서 사실대로 말해야 하는 시각은 예외 — 그건 플레이어가 쥐는 정황 증거다.
            if (who.IsKiller)
                for (int t = 0; t < N; t++)
                    if (!fixedSlots.ContainsKey(t) && claim[t] == c.MurderRoom) return false;

            // 거짓말한 시각의 방은 (틀이 요구하지 않는 한) 증인이 없는 방이어야 한다 — 여러 번 부딪히면 너무 쉽다
            bool travelBreak = false;
            for (int t = 1; t < N; t++)
            {
                int d = AlibiMap.Distance(claim[t - 1], claim[t]);
                if (d <= 1) continue;
                // 이동이 안 맞는 건 Travel 틀에서, 고정(목격된) 시각과 꾸민 시각 사이에서만 허용
                bool oneFixed = fixedSlots.ContainsKey(t - 1) ^ fixedSlots.ContainsKey(t);
                if (kind == LieKind.Travel && oneFixed && (t - 1 == Tm || t == Tm)) { travelBreak = true; continue; }
                return false;
            }

            foreach (int t in mustLie)
            {
                if (claim[t] == who.TruePath[t]) return false;
                bool witnessThere = c.Suspects.Any(o => o != who && !o.IsKiller && o.TruePath[t] == claim[t]);
                bool isTmTarget = t == Tm || (!who.IsKiller && t == who.SecretSlot);
                if (kind == LieKind.Presence && isTmTarget) { if (!witnessThere) return false; }
                else if (witnessThere) return false;
            }
            if (kind == LieKind.Travel && !travelBreak) return false;
            return true;
        }

        static bool PlantSecretLie(Random rng, AlibiCase c, bool chain)
        {
            var s = c.SecretLiar;
            int ts = s.SecretSlot;
            var kinds = new List<LieKind> { LieKind.Presence, LieKind.Travel, LieKind.Witness }.OrderBy(_ => rng.Next()).ToList();
            foreach (var kind in kinds)
            {
                // 비밀의 방에서 누가 레드헤링을 봤나(연쇄 사건에선 범인뿐 — 범인은 입 다문다)
                bool seenByInnocent = c.Suspects.Any(o => o != s && !o.IsKiller && o.TruePath[ts] == s.TruePath[ts]);
                if (kind == LieKind.Witness && !seenByInnocent) continue;
                if (kind != LieKind.Witness && seenByInnocent) continue; // 무고한 증인이 있는데 다른 틀을 쓰면 모순이 겹친다

                var fixedSlots = new Dictionary<int, string>();
                for (int t = 0; t < N; t++) if (t != ts) fixedSlots[t] = s.TruePath[t];
                var mustLie = new HashSet<int> { ts };
                var choice = SolveSecret(rng, c, s, fixedSlots, kind);
                if (choice == null) continue;
                s.Claims[ts] = new Claim(choice, Enumerable.Empty<string>());
                c.SecretLie = kind;
                return true;
            }
            return false;
        }

        static string SolveSecret(Random rng, AlibiCase c, Suspect s, Dictionary<int, string> fixedSlots, LieKind kind)
        {
            int ts = s.SecretSlot;
            var cands = new List<string>();
            foreach (var r in AlibiMap.Rooms.Select(x => x.Name))
            {
                if (r == s.TruePath[ts]) continue;
                if (ts == Tm && r == c.MurderRoom) continue;
                bool prevOk = ts == 0 || AlibiMap.Distance(fixedSlots[ts - 1], r) <= 1;
                bool nextOk = ts == N - 1 || AlibiMap.Distance(r, fixedSlots[ts + 1]) <= 1;
                // 그 시각 그 방에 있던 사람(범인 포함 — 범인도 그 시각은 사실대로 말할 수 있다)
                bool someoneThere = c.Suspects.Any(o => o != s && o.Claims[ts].Room == r && !o.LiesAt(ts));
                switch (kind)
                {
                    case LieKind.Presence:
                        if (prevOk && nextOk && someoneThere) cands.Add(r);
                        break;
                    case LieKind.Travel:
                        if ((!prevOk ^ !nextOk) && !someoneThere) cands.Add(r);
                        break;
                    case LieKind.Witness:
                        if (prevOk && nextOk && !someoneThere) cands.Add(r);
                        break;
                }
            }
            return cands.Count == 0 ? null : cands[rng.Next(cands.Count)];
        }

        // ───────────────────────── 3. 검증 ─────────────────────────

        public sealed class Report
        {
            public int TruthPairs;     // 진실끼리 부딪힘(있으면 생성기 버그)
            public int KillerDirect;   // 처음부터 범인 거짓말과 부딪히는 쌍
            public int SecretDirect;   // 처음부터 레드헤링 거짓말과 부딪히는 쌍
            public int KillerAfter;    // 레드헤링 고백 뒤 범인 거짓말과 부딪히는 쌍
        }

        public static List<StatementView> FullViews(AlibiCase c, bool secretConfessed)
        {
            var list = new List<StatementView>();
            foreach (var p in c.Suspects)
                for (int t = 0; t < c.SlotCount; t++)
                {
                    var cl = (secretConfessed && p.IsSecretLiar) ? p.Truth(t) : p.Claims[t];
                    list.Add(new StatementView { Person = p.Name, Slot = t, Room = cl.Room, SeenKnown = true, Seen = cl.Seen });
                }
            return list;
        }

        public static Report Analyze(AlibiCase c)
        {
            var r = new Report();
            bool IsLie(StatementView v, bool confessed)
            {
                var p = c.Get(v.Person);
                if (confessed && p.IsSecretLiar) return false;
                return p.LiesAt(v.Slot);
            }
            foreach (var (a, b, _) in ContradictionRules.FindAll(FullViews(c, false)))
            {
                var p = c.Get(a.Person); var q = c.Get(b.Person);
                bool la = IsLie(a, false), lb = IsLie(b, false);
                if (!la && !lb) r.TruthPairs++;
                if ((la && p.IsKiller) || (lb && q.IsKiller)) r.KillerDirect++;
                if ((la && p.IsSecretLiar) || (lb && q.IsSecretLiar)) r.SecretDirect++;
            }
            foreach (var (a, b, _) in ContradictionRules.FindAll(FullViews(c, true)))
            {
                var p = c.Get(a.Person); var q = c.Get(b.Person);
                bool la = IsLie(a, true), lb = IsLie(b, true);
                if (!la && !lb) r.TruthPairs++;
                if ((la && p.IsKiller) || (lb && q.IsKiller)) r.KillerAfter++;
            }
            return r;
        }

        static bool Verify(AlibiCase c)
        {
            var r = Analyze(c);
            if (r.TruthPairs > 0) return false;
            if (r.SecretDirect < 1) return false;
            if (c.ChainOnly) return r.KillerDirect == 0 && r.KillerAfter >= 1;
            return r.KillerDirect >= 1 && r.KillerDirect <= 4;
        }
    }
}
