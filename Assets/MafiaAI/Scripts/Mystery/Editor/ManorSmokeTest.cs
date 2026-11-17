using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using MafiaAI.Mystery.Manor;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MafiaAI.Mystery.EditorTools
{
    /// <summary>
    /// 저택 단면판 Play 모드 스모크(에디터 Play 중 MCP execute_code로 DriveInPlayMode(폴더, 단계)를 부른다).
    ///   stage "intro"  : 타이틀 → 프롤로그 → 저택 전경 → 현관 도착까지 찍는다
    ///   stage "full"   : 도입을 넘긴 뒤 실제로 걸어 다니며(계단·문) 조사·심문·제시·저택도·재구성·최후의 추궁·엔딩까지
    ///   stage "art"    : A/D 걷기(축 입력)의 속도·떨림을 재고, 집 밖 경계가 보이는 구도(양 끝 방·지하·저택도·번개)를 찍는다
    /// 장면마다 1920×1080 PNG와 smoke_log.txt를 남긴다.
    /// </summary>
    public static class ManorSmokeTest
    {
        static RenderTexture _rt;
        public static bool Running;
        public static string LastLog = "";

        public static async void DriveInPlayMode(string dir, string stage) => await Drive(dir, stage);

        static async Task Drive(string dir, string stage)
        {
            if (Running) return;
            Running = true;
            Application.runInBackground = true;
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            Camera cam = null;
            try
            {
                var game = await WaitFor(() => Object.FindFirstObjectByType<ManorGame>(), 20);
                cam = Camera.main;
                _rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = _rt;
                var s = game.EditorSession;

                if (stage == "intro")
                {
                    game.EditorAutoAdvance(true);
                    await Task.Delay(1800);
                    await Capture(cam, dir, "00_title", 0);
                    game.EditorSkipChapter();
                    await Task.Delay(3200);
                    await Capture(cam, dir, "01_prologue", 0);
                    for (int i = 0; i < 12 && game.cut.StoryOpen; i++) { game.cut.Advance(); await Task.Delay(450); game.cut.Advance(); await Task.Delay(450); }
                    await Task.Delay(1600);
                    await Capture(cam, dir, "02_establishing", 0);
                    await Task.Delay(3600);
                    await Capture(cam, dir, "03_arrival", 0);
                    await WaitUntil(() => game.EditorMode == "Explore", 60);
                    await Capture(cam, dir, "04_first_control", 600);
                    log.AppendLine("도입 OK · 모드 " + game.EditorMode + " · " + s.ClockLabel);
                }
                else if (stage == "late")
                {
                    // 일부러 늦게: 저택을 오가며 시간을 흘려 자정 · 6장 소각 · 두 번째 살인을 본다
                    game.EditorAutoAdvance(true);
                    await PassIntro(game);
                    bool midnightShot = false, murderShot = false;
                    for (int i = 0; i < 40 && s.Clock < MafiaAI.Mystery.MysteryCase.At(2, 5); i++)
                    {
                        game.EditorTravel(i % 2 == 0 ? "storage" : "study");
                        float t0 = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - t0 < 12f)
                        {
                            await Task.Delay(120);
                            if (!midnightShot && game.hud.chapterGroup.gameObject.activeSelf && game.hud.chapterGroup.alpha > 0.9f) { await Capture(cam, dir, "30_midnight", 0); midnightShot = true; }
                            if (!murderShot && game.cut.murderGroup.gameObject.activeSelf)
                            {
                                await Task.Delay(3000);
                                await Capture(cam, dir, "31_second_murder", 0);
                                murderShot = true;
                                game.cut.murderButton.onClick.Invoke();
                            }
                            if (!game.EditorBusy) break;
                        }
                    }
                    log.AppendLine($"늦은 탐정 {s.ClockLabel} · 6장 소각 {s.Has("ch6_burned")} · 세이 {(s.IsDead("세이") ? "사망" : "생존")}");
                    game.EditorTravel("greenhouse");
                    await Idle(game);
                    await Capture(cam, dir, "32_greenhouse_body", 1500);
                    game.EditorUse("gh_body");
                    await Idle(game);
                    await Capture(cam, dir, "33_body_examined", 900);
                    game.EditorMap(true);
                    await Task.Delay(1400);
                    await Capture(cam, dir, "34_map_late", 0);
                    game.EditorMap(false);
                    log.AppendLine($"수첩·사진: {s.HasEvidence("notebook")} {s.HasEvidence("photo")}");
                }
                else if (stage == "ux")
                {
                    // 소리 · 걷기(방향·박자) · 돋보기 · 사진 · 직접 묻기
                    game.EditorAutoAdvance(true);
                    await PassIntro(game);
                    await Task.Delay(2500);
                    log.AppendLine($"소리: 귀 {Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}개 · 출력 크기 {OutputRms():0.0000} (0이면 무음)");
                    int steps = 0;
                    System.Action onStep = () => steps++;
                    game.detective.actor.Step += onStep;
                    bool facedRight = true;
                    game.detective.editorAxis = 1f;
                    float t0 = Time.time;
                    while (Time.time - t0 < 2f) { await Task.Yield(); if (game.detective.actor.Moving && !game.detective.actor.body.flipX) facedRight = false; }
                    game.detective.editorAxis = 0f;
                    float dist = 2f * game.detective.actor.speed;
                    game.detective.actor.Step -= onStep;
                    log.AppendLine($"걷기: 오른쪽으로 걸을 때 그림도 오른쪽을 봄 {facedRight} · 2초 걸음 소리 {steps}번(예상 ≈ {dist / game.detective.actor.walkStride * 2f:0.0}) · 걷는 중 출력 {OutputRms():0.0000}");
                    await Task.Delay(400);
                    game.EditorLens(true);
                    await Capture(cam, dir, "60_lens_hall", 700);
                    game.EditorTravel("study"); await Idle(game);
                    await Capture(cam, dir, "61_lens_study", 1500);
                    game.EditorUse("study_watch");
                    await WaitUntil(() => game.EditorPhotoShown, 8);
                    await Task.Delay(500);
                    log.AppendLine("사진 뜸: " + game.EditorPhotoShown);
                    await Capture(cam, dir, "62_photo_watch", 0);
                    await Idle(game);
                    game.EditorUse("study_chair");
                    await WaitUntil(() => game.EditorPhotoShown, 8);
                    await Task.Delay(500);
                    await Capture(cam, dir, "63_photo_chair", 0);
                    await Idle(game);
                    game.EditorLens(false);
                    await Talk(game, log, "하루");
                    game.EditorAsk("h_rel"); await Idle(game);
                    foreach (var q in new[] { "대부분? 그게 무슨 뜻입니까?", "노아 씨 원고는 어떤 이야기였나요?" })
                    {
                        game.EditorFreeAsk(q);
                        await Idle(game);
                        await Task.Delay(600);
                        var last = s.Log.LastOrDefault(l => l.Speaker == "하루");
                        log.AppendLine($"  탐정: {q}\n  하루: {last?.Text}");
                    }
                    await Capture(cam, dir, "64_free_talk", 300);
                    game.EditorLeaveTalk(); await Idle(game);
                }
                else if (stage == "art")
                {
                    game.EditorAutoAdvance(true);
                    await PassIntro(game);
                    await Task.Delay(600);
                    game.EditorWalkTo(0.15f);
                    await WaitUntil(() => !game.EditorWalking, 20);
                    await Task.Delay(400);
                    await KeyWalk(game, log, cam, dir);
                    foreach (var (room, u, shot) in new[]
                    {
                        ("study", 0.06f, "41_study_left_edge"), ("dining", 0.94f, "42_dining_right_edge"),
                        ("greenhouse", 0.06f, "43_greenhouse_left_edge"), ("kitchen", 0.94f, "44_kitchen_right_edge"),
                        ("storage", 0.94f, "45_storage_right"), ("laundry", 0.3f, "46_laundry"),
                    })
                    {
                        game.EditorTravel(room);
                        await Idle(game);
                        game.EditorWalkTo(u);
                        await WaitUntil(() => !game.EditorWalking, 20);
                        await Idle(game);
                        await Capture(cam, dir, shot, 1800);
                        log.AppendLine($"{shot}: {s.Room} {s.ClockLabel}");
                    }
                    game.EditorTravel("study");
                    await Idle(game);
                    game.EditorWalkTo(0.66f);
                    await WaitUntil(() => !game.EditorWalking, 20);
                    await Idle(game);
                    await Capture(cam, dir, "47_study_noah", 1500);
                    game.EditorWalkTo(0.05f);
                    await WaitUntil(() => !game.EditorWalking, 20);
                    await Idle(game);
                    await Task.Delay(1500);
                    game.EditorFlash();
                    await WaitUntil(() => game.lightning.BoltVisible, 3);
                    await Capture(cam, dir, "48_lightning_edge", 0);
                    log.AppendLine("번개 줄기 보임: " + game.lightning.BoltVisible);
                    await Task.Delay(2500);
                    game.EditorMap(true);
                    await Task.Delay(1600);
                    await Capture(cam, dir, "49_overview", 0);
                    game.EditorFlash();
                    await WaitUntil(() => game.lightning.BoltVisible, 3);
                    await Capture(cam, dir, "50_overview_flash", 0);
                    game.EditorMap(false);
                }
                else
                {
                    game.EditorAutoAdvance(true);
                    await PassIntro(game);
                    await Task.Delay(600);
                    await Capture(cam, dir, "10_hall", 200);
                    log.AppendLine($"시작 {s.ClockLabel} · {s.Room}");

                    // 홀 계단 앞까지 걷기
                    game.EditorWalkTo(0.6f);
                    await Task.Delay(500);
                    await Capture(cam, dir, "11_walking", 0);
                    await WaitUntil(() => !game.EditorWalking, 15);
                    await Capture(cam, dir, "12_stair_prompt", 400);
                    game.EditorUse("stair:drawing");
                    await Idle(game);
                    await Capture(cam, dir, "13_drawing_room", 1500);
                    log.AppendLine($"계단 → {s.Room} {s.ClockLabel}");

                    // 연결문을 지나 서재로 걸어 들어간다
                    game.EditorWalkToRoom("study", 0.75f);
                    await Task.Delay(1400);
                    await Capture(cam, dir, "14_through_door", 0);
                    await WaitUntil(() => !game.EditorWalking, 20);
                    await Idle(game);
                    await Capture(cam, dir, "15_study", 1500);
                    log.AppendLine($"문 → {s.Room} {s.ClockLabel}");

                    foreach (var h in new[] { "study_watch", "study_paperweight", "study_card", "study_typewriter", "study_manuscript", "study_chair" })
                    {
                        game.EditorUse(h);
                        await Idle(game);
                        if (h == "study_watch") await Capture(cam, dir, "16_examine_watch", 700);
                    }
                    log.AppendLine($"서재 조사 {s.ClockLabel} · 증거 {s.Found.Count}");
                    game.EditorDetail("watch", "crown"); await Idle(game);
                    game.EditorDetail("paperweight", "top"); await Idle(game);

                    game.EditorFlash();
                    await Task.Delay(90);
                    await Capture(cam, dir, "17_lightning", 0);

                    // 제로 — 서재에서 시신 곁
                    game.EditorUse("talk:제로");
                    await Idle(game);
                    await Capture(cam, dir, "18_talk_zero", 900);
                    game.EditorSelectStatement("z_time");
                    await Task.Delay(500);
                    game.EditorSelectEvidence("watch");
                    await Capture(cam, dir, "19_present_book", 500);
                    game.EditorAct("watch");
                    await WaitUntil(() => game.cut.objectionGroup.gameObject.activeSelf, 10);
                    await Task.Delay(1300);
                    await Capture(cam, dir, "20_objection", 0);
                    game.cut.objectionButton.onClick.Invoke();
                    await Idle(game);
                    log.AppendLine($"제로 z_time 깸: {s.Has("broken:z_time")} {s.ClockLabel}");
                    game.EditorLeaveTalk(); await Idle(game);

                    // 저택도
                    game.EditorMap(true);
                    await Task.Delay(1400);
                    await Capture(cam, dir, "21_house_map", 0);
                    game.EditorMap(false);
                    await Task.Delay(800);

                    // 응접실 증거 → 홀의 제로(00:20 이후)
                    game.EditorWalkToRoom("drawing", 0.2f);
                    await WaitUntil(() => !game.EditorWalking, 20); await Idle(game);
                    foreach (var h in new[] { "drawing_bench", "drawing_rolls", "drawing_tools" }) { game.EditorUse(h); await Idle(game); }
                    game.EditorDetail("poker", "seam"); await Idle(game);
                    await Capture(cam, dir, "22_drawing_clues", 500);
                    game.EditorUse("stair:hall"); await Idle(game);
                    await Capture(cam, dir, "23_hall_again", 1200);
                    log.AppendLine($"홀 {s.ClockLabel} · 제로 위치 {s.LocationOf("제로")}");
                    if (s.LocationOf("제로") == s.Room)
                    {
                        game.EditorUse("talk:제로"); await Idle(game);
                        foreach (var (st, ev) in new[] { ("z_weapon", "poker"), ("z_piano", "roll"), ("z_secret", "ch6") })
                        {
                            game.EditorSelectStatement(st);
                            await Task.Delay(300);
                            game.EditorAct(ev);
                            await WaitUntil(() => game.cut.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                            if (game.cut.objectionGroup.gameObject.activeSelf) { await Task.Delay(800); game.cut.objectionButton.onClick.Invoke(); }
                            await Idle(game);
                            log.AppendLine($"  제로 {st} ← {ev}: {s.Has("broken:" + st)}");
                        }
                        game.EditorLeaveTalk(); await Idle(game);
                    }

                    // 식당의 미로(2층) — 저택도에서 바로 이동
                    await Talk(game, log, "미로");
                    game.EditorSelectStatement("m_study"); await Task.Delay(300);
                    game.EditorAct("card");
                    await WaitUntil(() => game.cut.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                    if (game.cut.objectionGroup.gameObject.activeSelf) { await Task.Delay(800); game.cut.objectionButton.onClick.Invoke(); }
                    await Idle(game);
                    await Capture(cam, dir, "24_miro_after", 800);
                    game.EditorLeaveTalk(); await Idle(game);

                    // 세이(홀) → 세탁실 → 세이
                    await Talk(game, log, "세이");
                    game.EditorSelectStatement("s_hall"); await Task.Delay(300);
                    game.EditorAct("t_miro");
                    await WaitUntil(() => game.cut.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                    if (game.cut.objectionGroup.gameObject.activeSelf) { await Task.Delay(800); game.cut.objectionButton.onClick.Invoke(); }
                    await Idle(game);
                    game.EditorLeaveTalk(); await Idle(game);
                    game.EditorDetail("photo", "bench"); await Idle(game);
                    game.EditorTravel("laundry"); await Idle(game);
                    game.EditorUse("laundry_basin"); await Idle(game);
                    await Capture(cam, dir, "25_laundry", 900);
                    await Talk(game, log, "세이");
                    game.EditorSelectStatement("s_hall2"); await Task.Delay(300);
                    game.EditorAct("gauze");
                    await WaitUntil(() => game.cut.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                    if (game.cut.objectionGroup.gameObject.activeSelf) { await Task.Delay(800); game.cut.objectionButton.onClick.Invoke(); }
                    await Idle(game);
                    game.EditorLeaveTalk(); await Idle(game);
                    log.AppendLine($"세이 증언 {s.HasEvidence("t_laundry")} · {s.ClockLabel}");

                    // 재구성 → 최후의 추궁
                    var answers = s.Case.Blanks.ToDictionary(b => b.Id, b => b.Answer);
                    game.EditorTheory(answers);
                    await Capture(cam, dir, "26_theory", 600);
                    game.EditorSubmitTheory();
                    await WaitUntil(() => game.EditorMode == "Talk" && !game.EditorBusy, 30);
                    await Capture(cam, dir, "27_confront", 800);
                    foreach (var ev in new[] { "roll", "t_laundry", "ch6" })
                    {
                        game.EditorAct(ev);
                        await WaitUntil(() => game.cut.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                        if (game.cut.objectionGroup.gameObject.activeSelf) { await Task.Delay(800); game.cut.objectionButton.onClick.Invoke(); }
                        await WaitUntil(() => !game.EditorBusy || game.cut.StoryOpen, 40);
                        if (game.cut.StoryOpen) break;
                    }
                    await WaitUntil(() => game.cut.StoryOpen || game.cut.ReportOpen, 30);
                    await Task.Delay(2500);
                    await Capture(cam, dir, "28_ending", 0);
                    for (int i = 0; i < 20 && !game.cut.ReportOpen; i++) { game.cut.Advance(); await Task.Delay(400); }
                    await Capture(cam, dir, "29_report", 1500);
                    log.AppendLine($"결말 {s.EndingId} · 해결 {s.Solved} · {s.ClockLabel}");
                }
                log.AppendLine("OK");
            }
            catch (Exception e)
            {
                log.AppendLine("실패: " + e);
            }
            if (cam != null) cam.targetTexture = null;
            LastLog = log.ToString();
            File.WriteAllText(Path.Combine(dir, "smoke_log.txt"), LastLog);
            Debug.Log("[ManorSmoke]\n" + LastLog);
            Running = false;
        }

        /// <summary>
        /// 오른쪽(D) 축 입력을 1.6초 넣어 — 키보드와 똑같은 조종 경로(DetectiveController.Update → 게임의 시간 흐름 → 그림 갱신) —
        /// 탐정이 걷는 모습을 프레임마다 잰다: 고른 속도인지 · 걷기 그림이 중간에 처음으로 되감기지 않는지 · 걷는 중 서는 자세가 끼지 않는지.
        /// </summary>
        static async Task KeyWalk(ManorGame game, StringBuilder log, Camera cam, string dir)
        {
            var actor = game.detective.actor;
            var xs = new List<float>();
            var dts = new List<float>();
            var walkIdx = new List<int>();
            int idleFrames = 0;
            try
            {
                game.detective.editorAxis = 1f;
                float t0 = Time.time;
                bool shot = false;
                while (Time.time - t0 < 1.6f)
                {
                    await Task.Yield();
                    xs.Add(actor.transform.position.x);
                    dts.Add(Time.deltaTime);
                    walkIdx.Add(Array.IndexOf(actor.walk, actor.body.sprite));
                    if (Time.time - t0 > 0.35f && !actor.Moving) idleFrames++;
                    if (!shot && Time.time - t0 > 0.9f) { shot = true; await Capture(cam, dir, "40_keywalk", 0); }
                }
            }
            finally { game.detective.editorAxis = 0f; }
            // 가속(0.35초) 뒤 구간만: 프레임별 속도
            var speeds = new List<float>();
            float acc = 0f;
            for (int i = 1; i < xs.Count; i++)
            {
                acc += dts[i];
                if (acc > 0.35f && dts[i] > 0f) speeds.Add((xs[i] - xs[i - 1]) / dts[i]);
            }
            int rewinds = 0;
            for (int i = 1; i < walkIdx.Count; i++)
                if (walkIdx[i] >= 0 && walkIdx[i - 1] >= 0 && walkIdx[i] < walkIdx[i - 1] && walkIdx[i - 1] != actor.walk.Length - 1) rewinds++;
            int backwards = 0;
            for (int i = 1; i < xs.Count; i++) if (xs[i] < xs[i - 1] - 0.0001f) backwards++;
            if (speeds.Count == 0) { log.AppendLine("A/D 측정: 움직임 없음"); return; }
            float mean = speeds.Average(), min = speeds.Min(), max = speeds.Max();
            log.AppendLine($"A/D 걷기: 평균 {mean:0.00}u/s (설정 {actor.speed:0.0}) · 프레임별 {min:0.00}~{max:0.00} · 뒤로 {backwards} · 걷기 그림 되감김 {rewinds} · 걷는 중 서기 {idleFrames} · 프레임 {xs.Count}");
        }

        /// <summary>귀(AudioListener)로 실제 나가는 소리의 크기(RMS) — 0이면 무음.</summary>
        static float OutputRms()
        {
            var buf = new float[2048];
            AudioListener.GetOutputData(buf, 0);
            double sum = 0;
            foreach (var v in buf) sum += v * v;
            return (float)System.Math.Sqrt(sum / buf.Length);
        }

        /// <summary>도입(장 제목 · 프롤로그 그림)을 넘겨 직접 조종할 때까지.</summary>
        static async Task PassIntro(ManorGame game)
        {
            var sw = Stopwatch.StartNew();
            while (game.EditorMode != "Explore")
            {
                if (sw.Elapsed.TotalSeconds > 90) throw new TimeoutException("도입을 넘기지 못함");
                if (game.hud.chapterGroup.gameObject.activeSelf) game.EditorSkipChapter();
                if (game.cut.StoryOpen) game.cut.Advance();
                await Task.Delay(350);
            }
        }

        static async Task Talk(ManorGame game, StringBuilder log, string name)
        {
            var s = game.EditorSession;
            // 가는 사이에 그 사람이 다른 방으로 옮겨 갈 수도 있다(동선) — 저택도로 다시 찾아간다
            for (int i = 0; i < 4 && s.LocationOf(name) != s.Room; i++)
            {
                log.AppendLine($"  → {name} 찾아 {s.LocationOf(name)}로 ({s.ClockLabel})");
                game.EditorTravel(s.LocationOf(name));
                await Idle(game);
                await Task.Delay(400);
            }
            game.EditorUse("talk:" + name);
            await Idle(game);
            if (game.EditorMode != "Talk") log.AppendLine($"  ⚠ {name}와 대화 못 함 (방 {s.Room}, {name} 위치 {s.LocationOf(name)})");
        }

        static async Task Idle(ManorGame game)
        {
            await Task.Delay(150);
            await WaitUntil(() => !game.EditorBusy, 60);
        }

        static async Task Capture(Camera cam, string dir, string name, int waitMs = 500)
        {
            if (waitMs > 0) await Task.Delay(waitMs);
            Canvas.ForceUpdateCanvases();
            var req = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = _rt; RenderPipeline.SubmitRenderRequest(cam, req); }
            else cam.Render();
            RenderTexture.active = _rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
        }

        static async Task<T> WaitFor<T>(Func<T> get, float seconds) where T : class
        {
            var sw = Stopwatch.StartNew();
            T v;
            while ((v = get()) == null)
            {
                if (sw.Elapsed.TotalSeconds > seconds) throw new TimeoutException("대기 시간 초과");
                await Task.Delay(100);
            }
            return v;
        }

        static async Task WaitUntil(Func<bool> cond, float seconds)
        {
            var sw = Stopwatch.StartNew();
            while (!cond())
            {
                if (sw.Elapsed.TotalSeconds > seconds) throw new TimeoutException("대기 시간 초과");
                await Task.Delay(100);
            }
        }
    }
}
