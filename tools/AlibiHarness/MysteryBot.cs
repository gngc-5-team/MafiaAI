using System;
using System.Collections.Generic;
using System.Linq;
using MafiaAI.Mystery;

namespace AlibiHarness
{
    /// <summary>
    /// 「자정의 원고」 검증.
    ///   dotnet run -- mystery check      데이터 무결성(참조·단어·풀이 가능성)
    ///   dotnet run -- mystery optimal    정답을 아는 탐정의 최단 경로(시각·사건 발생)
    ///   dotnet run -- mystery thorough   모든 곳을 살피고 다 묻는 꼼꼼한 탐정(첫 판 상한)
    ///   dotnet run -- mystery brute      무작정 증거를 들이미는 탐정이 쓰는 시간(노가다 차단 확인)
    ///   dotnet run -- mystery late       일부러 늦게 움직여 6장 소각·두 번째 살인 뒤에도 풀리는지
    /// </summary>
    public static class MysteryBot
    {
        public static int Run(string[] args)
        {
            string sub = args.Length > 1 ? args[1] : "all";
            int fails = 0;
            if (sub == "check" || sub == "all") fails += Check();
            if (sub == "optimal" || sub == "all") fails += Optimal(false);
            if (sub == "late" || sub == "all") fails += Optimal(true);
            if (sub == "thorough" || sub == "all") fails += Thorough();
            if (sub == "spatial" || sub == "all") fails += Spatial();
            if (sub == "brute" || sub == "all") Brute();
            Console.WriteLine(fails == 0 ? "\n== 전부 통과" : $"\n== 실패 {fails}건");
            return fails == 0 ? 0 : 1;
        }

        // ───────────────────────── 무결성 ─────────────────────────

        static int Check()
        {
            Console.WriteLine("== 데이터 무결성");
            var c = Case01.Build();
            int bad = 0;
            void Bad(string m) { bad++; Console.WriteLine("  ✗ " + m); }
            var evIds = new HashSet<string>(c.Evidence.Select(e => e.Id));
            var wordIds = new HashSet<string>(c.Words.Select(w => w.Id));
            var roomIds = new HashSet<string>(c.Rooms.Select(r => r.Id));
            string Base(string p) => p.Contains('@') ? p.Substring(0, p.IndexOf('@')) : p;

            if (c.Evidence.GroupBy(e => e.Id).Any(g => g.Count() > 1)) Bad("증거 id 중복");
            foreach (var r in c.Rooms)
                foreach (var h in r.Hotspots)
                {
                    if (!string.IsNullOrEmpty(h.Gives))
                        foreach (var g in h.Gives.Split(',')) if (!evIds.Contains(g.Trim())) Bad($"{h.Id} → 없는 증거 {g}");
                    foreach (var w in h.Words) if (!wordIds.Contains(w)) Bad($"{h.Id} → 없는 단어 {w}");
                    if (h.X < 0 || h.Y < 0 || h.X + h.W > 1.001f || h.Y + h.H > 1.001f) Bad($"{h.Id} 좌표가 그림 밖");
                }
            foreach (var cor in c.Corridors) foreach (var r in cor) if (!roomIds.Contains(r)) Bad("복도 → 없는 방 " + r);
            foreach (var e in c.Evidence)
            {
                foreach (var w in e.Words) if (!wordIds.Contains(w)) Bad($"{e.Id} → 없는 단어 {w}");
                foreach (var d in e.Details) foreach (var w in d.Words) if (!wordIds.Contains(w)) Bad($"{e.Id}.{d.Id} → 없는 단어 {w}");
            }
            foreach (var s in c.Suspects)
            {
                foreach (var st in s.Statements)
                {
                    foreach (var p in st.BrokenBy) if (!evIds.Contains(Base(p))) Bad($"{st.Id} → 없는 증거 {p}");
                    foreach (var p in st.Related) if (!evIds.Contains(p)) Bad($"{st.Id} → 없는 관련 증거 {p}");
                    foreach (var g in st.Gives) if (!evIds.Contains(g)) Bad($"{st.Id} → 없는 증거 {g}");
                    if (st.Hears != null && !evIds.Contains(st.Hears)) Bad($"{st.Id} → 없는 증언 {st.Hears}");
                    foreach (var w in st.Words) if (!wordIds.Contains(w)) Bad($"{st.Id} → 없는 단어 {w}");
                    if (st.Lie && st.BrokenBy.Length == 0) Bad($"{st.Id} 거짓말인데 깰 증거가 없음");
                    if (st.Lie && string.IsNullOrEmpty(st.Why)) Bad($"{st.Id} 컷신 설명(Why) 없음");
                }
                foreach (var k in s.Reactions.Keys) if (!evIds.Contains(k)) Bad($"{s.Name} 반응 → 없는 증거 {k}");
                foreach (var t in s.Topics) foreach (var g in t.Gives) if (!evIds.Contains(g)) Bad($"{t.Id} → 없는 증거 {g}");
            }
            foreach (var b in c.Blanks) if (!wordIds.Contains(b.Answer)) Bad($"빈칸 {b.Id} 정답 단어 없음");
            foreach (var b in c.Blanks) if (c.WordById(b.Answer)?.Kind != b.Kind) Bad($"빈칸 {b.Id} 종류 불일치");
            foreach (var reb in c.Rebuttals) foreach (var p in reb.Accept) if (!evIds.Contains(Base(p))) Bad("반박 → 없는 증거 " + p);
            foreach (var s in c.Sentences)
                foreach (var b in c.Blanks.Where(x => !s.Contains("{" + x.Id))) { }
            var used = c.Sentences.SelectMany(s => c.Blanks.Where(b => s.Contains("{" + b.Id))).Select(b => b.Id).Distinct().Count();
            if (used != c.Blanks.Length) Bad($"문장에 안 쓰인 빈칸이 있다({used}/{c.Blanks.Length})");

            // 얻을 수 없는 증거(어디서도 Gives/Hears 하지 않음)
            var obtainable = new HashSet<string>(c.Rooms.SelectMany(r => r.Hotspots).Where(h => h.Gives != null).SelectMany(h => h.Gives.Split(',').Select(x => x.Trim())));
            foreach (var st in c.AllStatements) { foreach (var g in st.St.Gives) obtainable.Add(g); if (st.St.Hears != null) obtainable.Add(st.St.Hears); }
            foreach (var e in c.Evidence) if (!obtainable.Contains(e.Id)) Bad($"얻을 길이 없는 증거 {e.Id}");

            // 증거 그림 확인(없는 그림 목록 — 아트 진행 상황)
            Console.WriteLine($"  증거 {c.Evidence.Length} · 조사 지점 {c.Rooms.Sum(r => r.Hotspots.Length)} · 거짓 진술 {c.AllStatements.Count(x => x.St.Lie)} · 의문점 {c.Questions.Length} · 단어 {c.Words.Length}");
            Console.WriteLine(bad == 0 ? "  ✓ 참조 무결" : $"  문제 {bad}건");
            return bad;
        }

        // ───────────────────────── 최단 경로 (저택 단면판: 사람을 찾아가야 말을 건다) ─────────────────────────

        static MysterySession NewSession(int seed)
        {
            var s = new MysterySession(Case01.Build(), seed) { RequirePresence = true };
            return s;
        }

        static int Optimal(bool late)
        {
            Console.WriteLine(late ? "\n== 늦은 탐정(소각·두 번째 살인 뒤에도 풀리나)" : "\n== 최단 경로(정답을 아는 탐정)");
            var s = NewSession(7);
            var c = s.Case;
            int fails = 0;
            void Do(ActionResult r, string label, Outcome? want = null)
            {
                bool ok = r.Outcome != Outcome.Refused && (want == null || r.Outcome == want);
                if (!ok) { fails++; Console.WriteLine($"  ✗ {label}: {r.Outcome} {r.Message}"); }
                else Console.WriteLine($"  {s.ClockLabel}  [{c.FloorLabel(s.Room)} {c.RoomById(s.Room).Name}] {label}{(r.Gained.Count > 0 ? "  +" + string.Join(",", r.Gained) : "")}");
                while (s.TryTakeEvent(out var ev)) Console.WriteLine($"     ▶ 사건 {ev.Event.Id}{(ev.Prevented ? " (막음)" : "")}: {ev.Event.CutSub ?? ev.Event.Toast}");
            }
            void Go(string room) { if (s.Room != room) Do(s.Move(room), "이동 → " + c.RoomById(room).Name); }
            void Find(string name) { var at = s.LocationOf(name); if (at != null) Go(at); if (!s.HasMet(name)) Do(s.Meet(name), "대면 " + name); }

            if (late)
            {
                // 일부러 시간을 흘린다: 손님을 찾아다니며 질문 쪽지를 전부 묻고, 저택을 배회한다
                foreach (var sus in c.Suspects) { Find(sus.Name); foreach (var t in s.Topics(sus.Name).ToList()) Do(s.Ask(sus.Name, t.Id), $"{sus.Name}에게 {t.Label}"); }
                for (int i = 0; i < 10 && s.Clock < MysteryCase.At(2, 20); i++) Go(i % 2 == 0 ? "storage" : "study");
            }

            Go("study");
            foreach (var h in new[] { "study_watch", "study_paperweight", "study_card", "study_typewriter", "study_manuscript", "study_chair" })
                Do(s.Examine(h), "조사 " + h);
            Do(s.InspectDetail("watch", "crown"), "확대: 시계 용두");
            Do(s.InspectDetail("paperweight", "top"), "확대: 문진의 피");
            Find("제로");
            Do(s.Present("제로", "z_time", "watch"), "제로 ← 회중시계", Outcome.Correct);
            Go("drawing");
            Do(s.Examine(late ? "drawing_bench_empty" : "drawing_bench"), "조사 피아노 의자");
            if (late) Do(s.Examine("drawing_fire_burned"), "조사 벽난로(타다 남은 조각)");
            Do(s.Examine("drawing_rolls"), "조사 롤 보관함");
            Do(s.Examine("drawing_tools"), "조사 벽난로 도구");
            Do(s.InspectDetail("poker", "seam"), "확대: 부지깽이 이음매");
            Find("제로");
            Do(s.Present("제로", "z_weapon", "poker"), "제로 ← 부지깽이", Outcome.Correct);
            Do(s.Present("제로", "z_piano", "roll"), "제로 ← 녹턴 롤", Outcome.Correct);
            Do(s.Present("제로", "z_secret", late ? "fragment" : "ch6"), "제로 ← 6장", Outcome.Correct);
            Find("미로");
            Do(s.Present("미로", "m_study", "card"), "미로 ← 카드", Outcome.Correct);
            if (!s.IsDead("세이"))
            {
                Find("세이");
                Do(s.Present("세이", "s_hall", "t_miro"), "세이 ← 미로의 23시 증언", Outcome.Correct);
                Do(s.InspectDetail("photo", "bench"), "확대: 사진 속 빈 의자");
            }
            Go("laundry");
            Do(s.Examine("laundry_basin"), "조사 대야");
            if (!s.IsDead("세이"))
            {
                Find("세이");
                Do(s.Present("세이", "s_hall2", "gauze"), "세이 ← 거즈", Outcome.Correct);
            }
            else
            {
                Go("greenhouse");
                Do(s.Examine("gh_body"), "조사 세이의 시신");
                Do(s.InspectDetail("photo", "bench"), "확대: 사진 속 빈 의자");
            }

            var answers = c.Blanks.ToDictionary(b => b.Id, b => b.Answer);
            foreach (var b in c.Blanks)
                if (!s.WordAvailable(c.WordById(b.Answer))) { fails++; Console.WriteLine($"  ✗ 정답 단어를 아직 못 얻음: {b.Id}={b.Answer}"); }
            Do(s.SubmitTheory(answers), "사건 재구성 제출", Outcome.Correct);
            Do(s.Rebut("roll"), "반박 1 ← 녹턴 롤", Outcome.Correct);
            Do(s.Rebut(s.HasEvidence("t_laundry") ? "t_laundry" : "notebook"), "반박 2 ← 22:55의 제로", Outcome.Correct);
            Do(s.Rebut(s.HasEvidence("ch6") ? "ch6" : "fragment"), "반박 3 ← 6장", Outcome.Correct);
            Console.WriteLine($"  결말: {s.EndingId} · 시각 {s.ClockLabel} · 해결 {s.Solved} · 세이 {(s.IsDead("세이") ? "사망" : "생존")}");
            if (!s.Solved) fails++;
            if (!late && s.EndingId != "witness") { fails++; Console.WriteLine("  ✗ 최단 경로인데 세이를 못 구함"); }
            if (late && !s.IsDead("세이")) Console.WriteLine("  (늦은 경로인데 세이가 살아 있음 — 일정 확인)");
            return fails;
        }

        // ───────────────────────── 꼼꼼한 탐정 ─────────────────────────

        static int Thorough()
        {
            Console.WriteLine("\n== 꼼꼼한 탐정(모든 방을 다 살피고, 손님을 찾아다니며 다 묻고 보여 줌 → 그다음 정답 제시)");
            var s = NewSession(3);
            var c = s.Case;
            void Drain() { while (s.TryTakeEvent(out var ev)) Console.WriteLine($"  {s.ClockLabel} ▶ {ev.Event.Id}{(ev.Prevented ? "(막음)" : "")}: {ev.Event.CutSub ?? ev.Event.Toast}"); }
            void Go(string room) { if (s.Room != room) s.Move(room); Drain(); }
            void Find(string name) { var at = s.LocationOf(name); if (at != null) Go(at); s.Meet(name); }
            foreach (var room in new[] { "hall", "greenhouse", "study", "drawing", "dining", "kitchen", "storage", "laundry" })
            {
                Go(room);
                foreach (var h in s.VisibleHotspots(room).ToList()) s.Examine(h.Id);
                Drain();
            }
            foreach (var e in s.FoundEvidence.ToList()) foreach (var d in e.Details) s.InspectDetail(e.Id, d.Id);
            Console.WriteLine($"  방 전부 조사 후: {s.ClockLabel}");
            foreach (var sus in c.Suspects)
            {
                if (s.IsDead(sus.Name)) continue;
                Find(sus.Name);
                foreach (var t in s.Topics(sus.Name).ToList()) s.Ask(sus.Name, t.Id);
                foreach (var ev in sus.Reactions.Keys.Where(s.HasEvidence).Take(4)) s.Show(sus.Name, ev);
                Drain();
            }
            Console.WriteLine($"  전원 심문(질문·보여 주기) 후: {s.ClockLabel}");
            var plan = new (string, string, string)[]
            {
                ("카이", "k_study", "lighter"), ("카이", "k_debt", "ledger"), ("미로", "m_study", "card"), ("미로", "m_candle", "candlestick"),
                ("미로", "m_secret", "ch3"), ("하루", "h_secret", "ch4"), ("제로", "z_time", "watch"), ("제로", "z_weapon", "poker"),
                ("제로", "z_piano", "roll"), ("제로", "z_secret", "ch6"), ("세이", "s_hall", "t_miro"), ("세이", "s_hall2", "gauze"), ("세이", "s_secret", "ch5"),
            };
            int ok = 0;
            foreach (var (who, st, ev) in plan)
            {
                if (s.IsDead(who)) continue;
                string use = ev == "ch6" && !s.HasEvidence("ch6") ? "fragment" : ev;
                Find(who);
                var r = s.Present(who, st, use);
                if (r.Outcome == Outcome.Correct) ok++;
                else Console.WriteLine($"  · {who}/{st} ← {use}: {r.Outcome} {r.Message}");
                Drain();
            }
            foreach (var e in s.FoundEvidence.ToList()) foreach (var d in e.Details) s.InspectDetail(e.Id, d.Id);
            if (s.IsDead("세이") && !s.HasEvidence("notebook")) { Go("greenhouse"); s.Examine("gh_body"); Drain(); }
            var answers = c.Blanks.ToDictionary(b => b.Id, b => b.Answer);
            var tr = s.SubmitTheory(answers);
            Console.WriteLine($"  진술 깸 {ok}/{plan.Length} · 재구성 {tr.Outcome} · 시각 {s.ClockLabel}");
            s.Rebut("roll");
            s.Rebut(s.HasEvidence("t_laundry") ? "t_laundry" : "notebook");
            s.Rebut(s.HasEvidence("ch6") ? "ch6" : "fragment");
            Console.WriteLine($"  결말 {s.EndingId} · {s.ClockLabel} · 찾은 증거 {s.Found.Count}/{c.Evidence.Length} · 의문점 해결 {s.SolvedQuestions.Count()}/{c.Questions.Length}");
            return s.Solved ? 0 : 1;
        }

        // ───────────────────────── 동선 · 막기 검증 ─────────────────────────

        static int Spatial()
        {
            Console.WriteLine("\n== 공간 규칙(같은 방에 있어야 대화 · 그 자리에 있으면 막는다)");
            int fails = 0;
            void Check(bool ok, string label) { if (!ok) { fails++; Console.WriteLine("  ✗ " + label); } else Console.WriteLine("  ✓ " + label); }
            var s = NewSession(11);
            var c = s.Case;
            Check(s.Meet("제로").Outcome == Outcome.Refused, "홀에서는 2층 서재의 제로에게 말을 걸 수 없다");
            Check(s.Meet("하루").Outcome != Outcome.Refused, "홀의 하루와는 대화할 수 있다");
            Check(c.TravelMinutes("hall", "study") == c.StairMinutes + c.WalkMinutesPerRoom, $"홀→서재 {c.TravelMinutes("hall", "study")}분(계단+한 칸)");
            Check(c.TravelMinutes("study", "laundry") > 0 && c.Path("study", "laundry").Count == 4, "서재→세탁실 길: " + string.Join("→", c.Path("study", "laundry")));
            // 걸음으로 시간이 흐른다
            int t0 = s.Clock;
            s.Walk(1.5f); s.Walk(1.5f);
            Check(s.Clock - t0 == 6, $"방 세 칸 걷기 = 6분 ({s.Clock - t0}분)");
            // 00:45 응접실을 지키면 소각을 막는다
            var a = NewSession(12);
            a.Move("drawing");
            while (a.Clock < MysteryCase.At(0, 46)) a.Walk(0.5f);
            Check(!a.Has("ch6_burned") && a.Has("burn_stopped"), "00:45 응접실에 있으면 6장 소각을 막는다");
            var b = NewSession(13);
            b.Move("kitchen");
            while (b.Clock < MysteryCase.At(0, 46)) b.Walk(0.5f);
            Check(b.Has("ch6_burned"), "자리를 비우면 00:45에 6장이 탄다");
            // 02:00 온실을 지키면 살인을 막는다
            var d = NewSession(14);
            d.Move("greenhouse");
            while (d.Clock < MysteryCase.At(2, 1)) d.Walk(0.5f);
            Check(!d.IsDead("세이"), "02:00 온실에 있으면 세이를 지킨다");
            Check(d.LocationOf("세이") == "greenhouse" || d.LocationOf("세이") == "dining", "세이의 동선: " + d.LocationOf("세이"));
            var e = NewSession(15);
            e.Move("storage");
            while (e.Clock < MysteryCase.At(2, 1)) e.Walk(0.5f);
            Check(e.IsDead("세이") && e.LocationOf("세이") == "greenhouse", "온실을 비우면 세이가 숨지고, 시신은 온실에 남는다");
            while (e.Clock < MysteryCase.At(2, 30)) e.Walk(0.5f);
            Check(e.LocationOf("세이") == "greenhouse", "숨진 뒤에는 동선이 이어져도 온실에 그대로");
            // 범인을 붙잡고 이야기하는 동안엔 범인이 아무 짓도 못 한다
            var h = NewSession(16);
            h.Move(h.LocationOf("제로"));
            h.Meet("제로");
            h.Hold("제로");
            while (h.Clock < MysteryCase.At(0, 46)) h.Walk(0.5f);
            Check(!h.Has("ch6_burned") && h.LocationOf("제로") == h.Room, "00:45에 제로를 붙잡고 있으면 6장이 타지 않고, 제로도 자리를 뜨지 않는다");
            h.Release("제로");
            Check(h.LocationOf("제로") == "drawing", "놓아주면 제로는 동선대로 응접실로 간다");
            return fails;
        }

        // ───────────────────────── 무작정 찍기 ─────────────────────────

        static void Brute()
        {
            Console.WriteLine("\n== 무작정 찍기(모든 증거를 가진 뒤, 거짓 진술마다 아무 증거나 들이밀 때)");
            var c = Case01.Build();
            int evCount = c.Evidence.Length;
            double total = 0;
            foreach (var (who, st) in c.AllStatements.Where(x => x.St.Lie))
            {
                int k = st.BrokenBy.Length;
                int near = st.BrokenBy.Count(p => p.Contains('@')) + st.Related.Length;
                double expectWrong = (double)(evCount - k) / (k + 1);
                double minutes = expectWrong * c.WrongCost;
                total += minutes;
                Console.WriteLine($"  {who.Name,-3} {st.Id,-10} 정답 {k}개 / 증거 {evCount}개 → 평균 헛짚기 {expectWrong:0.0}번 ≈ {minutes / 60:0.0}시간");
            }
            Console.WriteLine($"  합계 ≈ {total / 60:0.0}시간 (수사 시간은 {(c.Dawn - c.Start) / 60.0:0.0}시간) → 찍어서는 못 푼다");
            // 재구성 무작정: 빈칸 조합 수
            double combos = 1;
            foreach (var b in c.Blanks) combos *= c.Words.Count(w => w.Kind == b.Kind);
            Console.WriteLine($"  재구성 조합 {combos:N0}가지 · 틀릴 때마다 {c.WrongTheoryCost}분");
        }
    }
}
