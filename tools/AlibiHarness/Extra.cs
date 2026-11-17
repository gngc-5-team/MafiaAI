using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MafiaAI.Alibi;

namespace AlibiHarness
{
    /// <summary>하네스 전용 Ollama 어댑터(HttpClient). 유니티 쪽 OllamaClient와 같은 옵션(think:false, keep_alive)을 보낸다.</summary>
    sealed class HttpOllama : ILlm
    {
        readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        readonly string _model, _url;
        public int Calls;
        public long TotalMs;

        public HttpOllama(string model, string url = "http://localhost:11434") { _model = model; _url = url; }

        public async Task<string> GenerateAsync(string system, string prompt, float temperature, bool json, int maxTokens, CancellationToken ct)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = _model, ["prompt"] = prompt, ["system"] = system, ["stream"] = false,
                ["think"] = false, ["keep_alive"] = "30m",
                ["options"] = new Dictionary<string, object> { ["temperature"] = temperature, ["num_predict"] = maxTokens },
            };
            if (json) body["format"] = "json";
            var sw = Stopwatch.StartNew();
            var resp = await _http.PostAsync(_url + "/api/generate",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            Calls++; TotalMs += sw.ElapsedMilliseconds;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetProperty("response").GetString()?.Trim();
        }
    }

    public static partial class Extra
    {
        public static int Run(string cmd, string[] args)
        {
            int seed = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 1;
            string model = args.FirstOrDefault(a => a.StartsWith("model="))?.Substring(6) ?? "gemma4:e4b-it-qat";
            switch (cmd)
            {
                case "bot": return Bot(args.Length > 1 && int.TryParse(args[1], out var n) ? n : 300);
                case "llm": return Llm(seed, model).GetAwaiter().GetResult();
                case "play": return Play(seed, args.Contains("nollm") ? null : new HttpOllama(model)).GetAwaiter().GetResult();
                default:
                    Console.WriteLine("알 수 없는 명령: " + cmd);
                    return 2;
            }
        }

        // ───────────── 자동 탐정(템플릿 모드): 시계 안에 풀 수 있는 사건인가 ─────────────

        static int Bot(int count)
        {
            int solved = 0, confessed = 0, deadline = 0, second = 0, secondBeforeSolve = 0;
            var clocks = new List<int>();
            for (int seed = 1; seed <= count; seed++)
            {
                var c = CaseGenerator.Generate(seed);
                var ses = new AlibiSession(c, null, new SessionOptions { UseLlm = false });
                string verdict = SolveGreedy(ses);
                bool ok = ses.Result != null && ses.Result.Correct;
                if (ok) { solved++; clocks.Add(ses.Clock); }
                if (c.SecretLiar.Confessed) confessed++;
                if (verdict == "deadline") deadline++;
                if (ses.SecondMurderDone) { second++; if (ok) secondBeforeSolve++; }
                if (!ok && count <= 20) Console.WriteLine($"  seed {seed} 실패({verdict}) {(c.ChainOnly ? "연쇄" : "직접")} K={c.KillerLie} S={c.SecretLie} 시각 {ses.ClockLabel}");
            }
            Console.WriteLine($"자동 탐정 {count}판: 정답 {solved} ({100.0 * solved / count:F1}%)  마감 초과 {deadline}  두 번째 살인 발생 {second}(그중 정답 {secondBeforeSolve})  고백 유도 {confessed}");
            if (clocks.Count > 0)
            {
                clocks.Sort();
                string P(double q) => AlibiSession.FormatClock(clocks[(int)(q * (clocks.Count - 1))]);
                Console.WriteLine($"정답 판 지목 시각 중앙 {P(0.5)} · 75% {P(0.75)} · 90% {P(0.9)} · 최대 {AlibiSession.FormatClock(clocks.Max())}");
            }
            return 0;
        }

        /// <summary>
        /// 단순한 전략: ① 다섯 명과 첫 대면(동선 진술) ② 같은 방이 겹치는 칸마다 '누구 봤냐' ③ 모순은 바로 제시하고 양쪽 추궁
        /// ④ 고백·수첩으로 새로 드러난 칸으로 다시 탐색 ⑤ 해명 못 한 사람을 지목.
        /// </summary>
        static string SolveGreedy(AlibiSession ses)
        {
            var c = ses.Case;
            foreach (var p in c.Suspects)
                if (ses.CanAct && !p.Dead) ses.MeetAsync(p.Name).GetAwaiter().GetResult();

            for (int round = 0; round < 6 && ses.CanAct; round++)
            {
                PresentAll(ses);
                PressAll(ses);
                var evader = EvaderName(ses);
                if (evader != null) { ses.Accuse(evader); return "ok"; }
                if (!ses.CanAct) break;

                bool asked = false;
                for (int t = 0; t < c.SlotCount && ses.CanAct; t++)
                {
                    var groups = c.Suspects.Where(p => ses.IsKnown(new CellRef(p.Name, t)))
                        .GroupBy(p => ses.View(new CellRef(p.Name, t)).Room).Where(g => g.Count() >= 2);
                    foreach (var g in groups)
                    {
                        var target = g.FirstOrDefault(p => !p.Dead && !ses.View(new CellRef(p.Name, t)).SeenKnown);
                        if (target == null) continue;
                        ses.AskAsync(target.Name, $"{AlibiCase.TimeLabel(t)}에 누구 봤어?", default, true).GetAwaiter().GetResult();
                        asked = true;
                        if (!ses.CanAct) break;
                    }
                }
                if (!asked && ses.CanAct)
                {
                    var next = c.Suspects.Where(p => !p.Dead).SelectMany(p => Enumerable.Range(0, c.SlotCount).Select(t => new CellRef(p.Name, t)))
                        .FirstOrDefault(r => !ses.View(r).SeenKnown);
                    if (next.Person == null) break;
                    ses.AskAsync(next.Person, $"{AlibiCase.TimeLabel(next.Slot)}에 누구 봤어?", default, true).GetAwaiter().GetResult();
                }
            }
            if (ses.CanAct) { PresentAll(ses); PressAll(ses); }
            var last = EvaderName(ses);
            if (last != null) { ses.Accuse(last); return "ok"; }
            var guess = ses.Contradictions.SelectMany(r => new[] { r.A.Person, r.B.Person })
                .Where(n => !c.Get(n).Confessed && !c.Get(n).Dead).GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                ?? c.Suspects.First(p => !p.Dead).Name;
            ses.Accuse(guess);
            return ses.DeadlineReached ? "deadline" : "stuck";
        }

        static void PresentAll(AlibiSession ses)
        {
            var c = ses.Case;
            var known = c.Suspects.SelectMany(p => Enumerable.Range(0, c.SlotCount).Select(t => new CellRef(p.Name, t))).Where(ses.IsKnown).ToList();
            for (int i = 0; i < known.Count && ses.CanAct; i++)
                for (int j = i + 1; j < known.Count; j++)
                    if (ContradictionRules.Check(ses.View(known[i]), ses.View(known[j])).Valid)
                        ses.Present(known[i], known[j]);
        }

        static void PressAll(AlibiSession ses)
        {
            foreach (var r in ses.Contradictions.ToList())
                foreach (var who in new[] { r.A.Person, r.B.Person }.Distinct())
                    if (ses.CanPress(who, r) && EvaderName(ses) == null)
                        ses.PressAsync(who, r.Id).GetAwaiter().GetResult();
        }

        static string EvaderName(AlibiSession ses) =>
            ses.Contradictions.SelectMany(r => r.Reactions).Where(kv => kv.Value == PressReaction.Evade).Select(kv => kv.Key).FirstOrDefault();

        // ───────────── 실제 LLM 시나리오 ─────────────

        static async Task<int> Llm(int seed, string model)
        {
            var c = CaseGenerator.Generate(seed);
            Program.Show(c);
            var llm = new HttpOllama(model);
            var ses = new AlibiSession(c, llm, new SessionOptions { DeadlineAt = 9999, SecondMurderAt = 9999 });
            var questions = new[]
            {
                "비명 들렸을 때 어디서 누구랑 있었어?",
                "그 전엔 누구 봤어?",
                "10시 45분엔 누구랑 있었지?",
                $"{c.Victim}랑은 무슨 사이였어?",
                "솔직히 네가 범인이지?",
                "부엌에서 누구 본 적 있어?",
                "누가 범인 같아?",
            };
            int fallback = 0, total = 0;
            foreach (var p in c.Suspects.Take(3))
            {
                Console.WriteLine($"\n──── {p.Name}{(p.IsKiller ? " (범인)" : p.IsSecretLiar ? " (비밀)" : "")} ────");
                {
                    var sw0 = Stopwatch.StartNew();
                    var t0 = await ses.MeetAsync(p.Name);
                    total++;
                    if (ses.LastSource == "template") fallback++;
                    Console.WriteLine($"[첫 진술] {p.Name}: {t0}   [{ses.LastSource} {sw0.ElapsedMilliseconds}ms]{(ses.LastProblem != null ? "  ⚠ " + ses.LastProblem : "")}");
                }
                foreach (var q in questions)
                {
                    var sw = Stopwatch.StartNew();
                    var a = await ses.AskAsync(p.Name, q);
                    total++;
                    if (ses.LastSource == "template") fallback++;
                    Console.WriteLine($"탐정: {q}");
                    Console.WriteLine($"   {ses.LastIntent}");
                    Console.WriteLine($"{p.Name}: {a}   [{ses.LastSource} {sw.ElapsedMilliseconds}ms]{(ses.LastProblem != null ? "  ⚠ " + ses.LastProblem : "")}");
                }
            }

            // 대표 모순을 찾아 제시하고 양쪽 추궁(정체 반응 확인)
            Console.WriteLine("\n──── 모순 제시·추궁 ────");
            var allRefs = c.Suspects.SelectMany(p => Enumerable.Range(0, c.SlotCount).Select(t => new CellRef(p.Name, t))).ToList();
            foreach (var p in c.Suspects)
                await ses.MeetAsync(p.Name);
            foreach (var p in c.Suspects)
                for (int t = 0; t < c.SlotCount; t++)
                    if (!ses.View(new CellRef(p.Name, t)).SeenKnown) await ses.AskAsync(p.Name, $"{AlibiCase.TimeLabel(t)}에 누구 봤어?");
            for (int i = 0; i < allRefs.Count; i++)
                for (int j = i + 1; j < allRefs.Count; j++)
                    if (ContradictionRules.Check(ses.View(allRefs[i]), ses.View(allRefs[j])).Valid) ses.Present(allRefs[i], allRefs[j]);
            foreach (var r in ses.Contradictions.ToList())
            {
                Console.WriteLine($"[모순] {r.Explanation}");
                foreach (var who in new[] { r.A.Person, r.B.Person }.Distinct())
                {
                    if (!ses.CanPress(who, r)) continue;
                    var o = await ses.PressAsync(who, r.Id);
                    total++;
                    if (ses.LastSource == "template") fallback++;
                    Console.WriteLine($"   → {who} {o.Reaction}: {o.Line}   [{ses.LastSource}]{(ses.LastProblem != null ? "  ⚠ " + ses.LastProblem : "")}");
                }
            }
            Console.WriteLine($"\nLLM 호출 {llm.Calls}회 평균 {(llm.Calls > 0 ? llm.TotalMs / llm.Calls : 0)}ms · 대사 {total}개 중 템플릿 대체 {fallback}개");
            return 0;
        }

        // ───────────── 콘솔 플레이 ─────────────

        static async Task<int> Play(int seed, ILlm llm)
        {
            var c = CaseGenerator.Generate(seed);
            var ses = new AlibiSession(c, llm);
            Console.WriteLine(ses.Briefing().Replace("<b>", "").Replace("</b>", ""));
            Console.WriteLine("\n명령: [이름] 질문 | 제시 이름 시각 이름 시각 | 추궁 이름 번호 | 지목 이름 | 보드 | 끝");
            while (!ses.Over)
            {
                while (ses.Events.Count > 0)
                {
                    var ev = ses.Events.Dequeue();
                    Console.WriteLine(ev.Kind == SessionEventKind.SecondMurder ? $"\n★ 두 번째 비명 — {ev.Victim}, {ev.Room}. 수첩: {ev.Note}" : "\n★ 범인이 달아나려 한다 — 지목하라");
                }
                Console.Write($"\n({ses.ClockLabel}) > ");
                var line = Console.ReadLine();
                if (line == null || line == "끝") break;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (parts[0] == "보드") { PrintBoard(ses); continue; }
                if (parts[0] == "제시" && parts.Length >= 5)
                {
                    var a = new CellRef(parts[1], Array.IndexOf(AlibiCase.SlotTimes, parts[2]));
                    var b = new CellRef(parts[3], Array.IndexOf(AlibiCase.SlotTimes, parts[4]));
                    var o = ses.Present(a, b);
                    Console.WriteLine((o.Valid ? "[모순 성립] " : "[아님] ") + o.Message);
                    continue;
                }
                if (parts[0] == "추궁" && parts.Length >= 3)
                {
                    var o = await ses.PressAsync(parts[1], int.Parse(parts[2]));
                    Console.WriteLine(o == null ? "추궁할 수 없다." : $"{parts[1]} ({o.Reaction}): {o.Line}");
                    continue;
                }
                if (parts[0] == "지목" && parts.Length >= 2)
                {
                    Console.WriteLine(ses.Accuse(parts[1]).Text.Replace("<b>", "").Replace("</b>", ""));
                    break;
                }
                if (c.Get(parts[0]) != null)
                {
                    var q = line.Substring(parts[0].Length).Trim();
                    if (!ses.HasMet(parts[0])) Console.WriteLine($"{parts[0]}(첫 진술): {await ses.MeetAsync(parts[0])}");
                    if (q.Length == 0) continue;
                    var a = await ses.AskAsync(parts[0], q);
                    Console.WriteLine($"{parts[0]}: {a}");
                    Console.WriteLine($"   ({ses.LastIntent} · {ses.LastSource}{(ses.LastProblem != null ? " · " + ses.LastProblem : "")})");
                }
            }
            return 0;
        }

        static void PrintBoard(AlibiSession ses)
        {
            var c = ses.Case;
            Console.WriteLine("        " + string.Join(" | ", AlibiCase.SlotTimes.Select(t => t.PadRight(14))));
            for (int i = 0; i < c.Suspects.Count; i++)
            {
                var cells = Enumerable.Range(0, c.SlotCount).Select(t =>
                {
                    var st = ses.Cell(i, t);
                    if (!st.LocationKnown) return "?".PadRight(14);
                    string seen = st.SeenKnown ? (st.Seen.Count == 0 ? "[-]" : "[" + string.Join(",", st.Seen) + "]") : "";
                    return (st.Room + seen + (st.InContradiction ? "!" : "")).PadRight(14);
                });
                Console.WriteLine($"{c.Suspects[i].Name,-6}  " + string.Join(" | ", cells));
            }
            foreach (var r in ses.Contradictions) Console.WriteLine($"  #{r.Id} {r.Label}: {r.Explanation}");
        }
    }
}
