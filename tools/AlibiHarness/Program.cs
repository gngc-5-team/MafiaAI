using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MafiaAI.Alibi;

namespace AlibiHarness
{
    /// <summary>
    /// 사용법
    ///   dotnet run -- gen [개수]        사건 대량 생성 통계(풀이 가능성·거짓말 틀 분포)
    ///   dotnet run -- show [seed]       사건 하나의 진실·진술·모순 출력
    ///   dotnet run -- llm [seed]        실제 Ollama로 질문 시나리오 실행(프롬프트·검증 확인)
    ///   dotnet run -- play [seed]       콘솔에서 직접 플레이(템플릿/LLM)
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string cmd = args.Length > 0 ? args[0] : "gen";
            int n = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 0;
            switch (cmd)
            {
                case "mystery": return MysteryBot.Run(args);
                case "talk": return MysteryTalkTest.Run(args);
                case "gen": return Gen(n > 0 ? n : 500);
                case "show": Show(CaseGenerator.Generate(n)); return 0;
                case "find":
                    {
                        // dotnet run -- find Travel  → 범인 거짓말 틀이 Travel인 첫 사건
                        var want = args.Length > 1 ? args[1] : "Travel";
                        for (int seed = 1; seed < 5000; seed++)
                        {
                            var c = CaseGenerator.Generate(seed);
                            if (c.KillerLie.ToString() == want || (want == "Chain" && c.ChainOnly)) { Show(c); return 0; }
                        }
                        return 1;
                    }
                default:
                    return Extra.Run(cmd, args);
            }
        }

        static int Gen(int count)
        {
            var kinds = new Dictionary<string, int>();
            int chain = 0, fail = 0, truthBug = 0;
            var kDirect = new List<int>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int seed = 1; seed <= count; seed++)
            {
                AlibiCase c;
                try { c = CaseGenerator.Generate(seed); }
                catch (Exception e) { fail++; Console.WriteLine("FAIL " + seed + ": " + e.Message); continue; }
                var r = CaseGenerator.Analyze(c);
                if (r.TruthPairs > 0) truthBug++;
                string key = (c.ChainOnly ? "연쇄/" : "직접/") + c.KillerLie + " + 비밀/" + c.SecretLie;
                kinds[key] = kinds.TryGetValue(key, out var v) ? v + 1 : 1;
                if (c.ChainOnly) chain++;
                kDirect.Add(r.KillerDirect);
            }
            Console.WriteLine($"{count}건 생성 {sw.ElapsedMilliseconds}ms  실패 {fail}  진실끼리 모순(버그) {truthBug}  연쇄 {chain}");
            foreach (var kv in kinds.OrderByDescending(x => x.Value)) Console.WriteLine($"  {kv.Key,-40} {kv.Value}");
            Console.WriteLine("범인 직접 모순 쌍 분포: " + string.Join(" ", kDirect.GroupBy(x => x).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
            return fail == 0 && truthBug == 0 ? 0 : 1;
        }

        public static void Show(AlibiCase c)
        {
            Console.WriteLine($"=== 사건 #{c.Seed}  피해자 {c.Victim} · {c.MurderRoom} · 흉기 {c.Weapon}({c.WeaponRoom}, {AlibiCase.TimeLabel(c.WeaponSlot)}에 챙김) · {(c.ChainOnly ? "연쇄" : "직접")}");
            Console.WriteLine($"    범인 {c.Killer.Name}({c.KillerLie})  레드헤링 {c.SecretLiar.Name}({c.SecretLie}) 비밀: {AlibiCase.TimeLabel(c.SecretLiar.SecretSlot)} {c.SecretLiar.SecretRoom} — {c.SecretLiar.Secret}");
            Console.WriteLine("    피해자 동선: " + string.Join(" → ", c.VictimPath.Where(x => x != null)));
            foreach (var p in c.Suspects)
            {
                var sb = new StringBuilder();
                sb.Append($"  {p.Name}{(p.IsKiller ? "*" : p.IsSecretLiar ? "?" : " ")} ");
                for (int t = 0; t < c.SlotCount; t++)
                {
                    var cl = p.Claims[t];
                    bool lie = p.LiesAt(t);
                    string seen = cl.Seen.Count == 0 ? "-" : string.Join(",", cl.Seen);
                    sb.Append($"| {AlibiCase.TimeLabel(t)} {cl.Room}[{seen}]");
                    if (lie) sb.Append($"(진실 {p.TruePath[t]}[{string.Join(",", p.TrueSeen[t])}])");
                    sb.Append(' ');
                }
                Console.WriteLine(sb.ToString());
            }
            Console.WriteLine("  -- 처음부터 성립하는 모순");
            foreach (var (a, b, v) in ContradictionRules.FindAll(CaseGenerator.FullViews(c, false)))
                Console.WriteLine($"    [{v.Kind}] {a.Person}@{AlibiCase.TimeLabel(a.Slot)} × {b.Person}@{AlibiCase.TimeLabel(b.Slot)} : {v.Explanation}");
            Console.WriteLine("  -- 레드헤링 고백 뒤 새로 생기는 모순");
            string Key(StatementView a, StatementView b) => a.Person + a.Slot + "|" + b.Person + b.Slot;
            var before = new HashSet<string>(ContradictionRules.FindAll(CaseGenerator.FullViews(c, false)).Select(x => Key(x.A, x.B)));
            foreach (var (a, b, v) in ContradictionRules.FindAll(CaseGenerator.FullViews(c, true)))
                if (!before.Contains(Key(a, b)))
                    Console.WriteLine($"    [{v.Kind}] {a.Person}@{AlibiCase.TimeLabel(a.Slot)} × {b.Person}@{AlibiCase.TimeLabel(b.Slot)} : {v.Explanation}");
        }
    }
}
