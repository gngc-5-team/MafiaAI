using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using MafiaAI.Mystery.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MafiaAI.Mystery.EditorTools
{
    /// <summary>
    /// 「자정의 원고」 Play 모드 스모크. 에디터 Play 중에 DriveInPlayMode(폴더, 빠른길)를 부른다(MCP execute_code).
    ///   fast=true  : 정답을 아는 탐정의 경로(프롤로그 → 서재 조사 → 확대 → 응접실 → 심문·모순 → 재구성 → 최후의 추궁 → 엔딩·보고서)
    ///   fast=false : 일부러 시간을 흘려 6장 소각·두 번째 살인 컷신까지 본다
    /// 장면마다 PNG(1920×1080)를 남기고 smoke_log.txt에 진행을 적는다.
    /// </summary>
    public static class MysterySmokeTest
    {
        static RenderTexture _rt;
        public static bool Running;
        public static string LastLog = "";

        public static async void DriveInPlayMode(string dir, bool fast) => await Drive(dir, fast);

        static async Task Drive(string dir, bool fast)
        {
            if (Running) return;
            Running = true;
            Application.runInBackground = true;
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            try
            {
                var game = await WaitFor(() => Object.FindFirstObjectByType<MysteryGame>(), 20);
                var cam = Camera.main;
                _rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = _rt;
                game.EditorAutoAdvance(true);
                var hud = game.hud;

                // 프롤로그
                await WaitUntil(() => hud.StoryOpen, 10);
                await Task.Delay(2600);
                await Capture(cam, dir, "01_prologue");
                int guard = 0;
                while (hud.StoryOpen && guard++ < 40) { hud.Advance(); await Task.Delay(350); if (guard == 4) await Capture(cam, dir, "01b_prologue_dinner", 1500); }
                await Idle(game);
                var s = game.EditorSession;
                log.AppendLine($"시작 {s.ClockLabel} · 방 {s.Room}");
                await Capture(cam, dir, "02_study", 1200);

                game.room.PreviewHover("study_watch");
                await Capture(cam, dir, "03_hover", 200);

                if (!fast)
                {
                    // 일부러 늦게: 질문만 잔뜩 하고 배회
                    foreach (var n in new[] { "카이", "제로", "미로", "하루", "세이" })
                    {
                        game.EditorOpen(n); await Idle(game);
                        foreach (var t in s.Topics(n).ToList()) { game.EditorAsk(t.Id); await Idle(game); }
                    }
                    game.EditorRoom(); await Idle(game);
                    for (int i = 0; i < 8 && !s.IsDead("세이"); i++)
                    {
                        game.EditorMove(i % 2 == 0 ? "storage" : "study");
                        await WaitUntil(() => !game.EditorBusy || game.hud.murderGroup.gameObject.activeSelf, 40);
                        if (game.hud.murderGroup.gameObject.activeSelf)
                        {
                            await Task.Delay(3200);
                            await Capture(cam, dir, "20_second_murder", 0);
                            game.hud.murderButton.onClick.Invoke();
                        }
                        await Idle(game);
                    }
                    log.AppendLine($"늦은 탐정: {s.ClockLabel} · 6장 소각 {s.Has("ch6_burned")} · 세이 {(s.IsDead("세이") ? "사망" : "생존")}");
                    game.EditorMove("greenhouse"); await Idle(game);
                    await Capture(cam, dir, "21_greenhouse_after", 1200);
                }

                foreach (var h in new[] { "study_watch", "study_paperweight", "study_card", "study_typewriter", "study_manuscript", "study_chair" })
                {
                    if (s.Room != "study") { game.EditorMove("study"); await Idle(game); }
                    game.EditorExamine(h);
                    await Idle(game);
                    if (h == "study_watch") await Capture(cam, dir, "04_examine_watch", 1500);
                }
                log.AppendLine($"서재 조사 후 {s.ClockLabel} · 증거 {s.Found.Count}");

                game.EditorInspect("watch");
                await Capture(cam, dir, "05_viewer_watch", 600);
                game.EditorDetail("watch", "crown");
                await Idle(game);
                await Capture(cam, dir, "06_detail_crown", 900);
                game.EditorCloseOverlays();
                game.EditorDetail("paperweight", "top"); await Idle(game);

                game.EditorRead("ch4");
                await Capture(cam, dir, "07_reader_ch4", 600);
                game.EditorCloseOverlays();

                game.EditorMove("drawing"); await Idle(game);
                await Capture(cam, dir, "08_drawing", 1400);
                foreach (var h in new[] { fast ? "drawing_bench" : "drawing_fire_burned", "drawing_rolls", "drawing_tools" })
                {
                    game.EditorExamine(h);
                    await Idle(game);
                }
                game.EditorDetail("poker", "seam"); await Idle(game);
                game.EditorSelectEvidence("poker");
                await Capture(cam, dir, "09_book_poker", 800);

                // 심문
                game.EditorLineup(); await Idle(game);
                await Capture(cam, dir, "10_lineup", 800);
                // 자유 질문(LLM)
                game.EditorOpen("카이"); await Idle(game);
                var sw = Stopwatch.StartNew();
                game.EditorFreeAsk("노아랑 무슨 사이였어? 오늘 밤 노아한테 화났던 적 있어?");
                await Idle(game);
                await Capture(cam, dir, "11a_free_question", 1800);
                log.AppendLine($"자유 질문(카이): {s.Log.LastOrDefault()?.Text}  [{sw.ElapsedMilliseconds}ms]");
                game.EditorOpen("미로"); await Idle(game);
                await Capture(cam, dir, "11_talk_miro", 1500);
                game.EditorSelectStatement("m_study");
                game.EditorSelectEvidence("card");
                await Capture(cam, dir, "12_select_present", 500);
                game.EditorAct("card");
                await WaitUntil(() => game.hud.objectionGroup.gameObject.activeSelf, 10);
                await Task.Delay(1400);
                await Capture(cam, dir, "13_objection", 0);
                game.hud.objectionButton.onClick.Invoke();
                await Idle(game);
                await Capture(cam, dir, "14_after_break", 1500);
                log.AppendLine($"미로 m_study 깸: {s.Has("broken:m_study")} · {s.ClockLabel}");

                // 제로
                game.EditorOpen("제로"); await Idle(game);
                foreach (var (st, ev) in new[] { ("z_time", "watch"), ("z_weapon", "poker"), ("z_piano", "roll"), ("z_secret", fast ? "ch6" : "fragment") })
                {
                    game.EditorSelectStatement(st);
                    game.EditorAct(ev);
                    await WaitUntil(() => game.hud.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                    if (game.hud.objectionGroup.gameObject.activeSelf) { await Task.Delay(900); game.hud.objectionButton.onClick.Invoke(); }
                    await Idle(game);
                    log.AppendLine($"제로 {st} ← {ev}: {s.Has("broken:" + st)}");
                }
                await Capture(cam, dir, "15_zero_broken", 800);

                // 세이
                if (!s.IsDead("세이"))
                {
                    game.EditorOpen("세이"); await Idle(game);
                    game.EditorSelectStatement("s_hall"); game.EditorAct("t_miro");
                    await WaitUntil(() => game.hud.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                    if (game.hud.objectionGroup.gameObject.activeSelf) { await Task.Delay(900); game.hud.objectionButton.onClick.Invoke(); }
                    await Idle(game);
                    game.EditorInspect("photo");
                    game.EditorDetail("photo", "bench"); await Idle(game);
                    await Capture(cam, dir, "16_photo_bench", 900);
                    game.EditorCloseOverlays();
                }
                game.EditorRoom(); await Idle(game);
                game.EditorMove("laundry"); await Idle(game);
                game.EditorExamine("laundry_basin"); await Idle(game);
                await Capture(cam, dir, "17_laundry", 1200);
                if (!s.IsDead("세이"))
                {
                    game.EditorOpen("세이"); await Idle(game);
                    game.EditorSelectStatement("s_hall2"); game.EditorAct("gauze");
                    await WaitUntil(() => game.hud.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                    if (game.hud.objectionGroup.gameObject.activeSelf) { await Task.Delay(900); game.hud.objectionButton.onClick.Invoke(); }
                    await Idle(game);
                }
                else
                {
                    game.EditorMove("greenhouse"); await Idle(game);
                    game.EditorExamine("gh_body"); await Idle(game);
                    game.EditorDetail("photo", "bench"); await Idle(game);
                }

                // 타임라인·의문점
                game.book.SetTab(1);
                await Capture(cam, dir, "18_timeline", 600);
                game.book.SetTab(3);
                await Capture(cam, dir, "19_questions", 600);
                game.book.SetTab(0);

                // 재구성
                var answers = s.Case.Blanks.ToDictionary(b => b.Id, b => b.Answer);
                game.EditorTheory(answers);
                await Capture(cam, dir, "22_theory", 800);
                game.theory.OpenPicker("time");
                await Capture(cam, dir, "22b_picker", 400);
                game.theory.pickerRoot.SetActive(false);
                game.EditorSubmitTheory();
                await Idle(game);
                await Capture(cam, dir, "23_confront", 1500);
                log.AppendLine($"재구성: {s.Has("theory_ok")} · 단계 {s.Phase}");

                foreach (var ev in new[] { "roll", s.HasEvidence("t_laundry") ? "t_laundry" : "notebook", s.HasEvidence("ch6") ? "ch6" : "fragment" })
                {
                    game.EditorAct(ev);
                    await WaitUntil(() => game.hud.objectionGroup.gameObject.activeSelf || !game.EditorBusy, 10);
                    if (game.hud.objectionGroup.gameObject.activeSelf) { await Task.Delay(900); game.hud.objectionButton.onClick.Invoke(); }
                    await WaitUntil(() => !game.EditorBusy || hud.StoryOpen, 30);
                    if (hud.StoryOpen) break;
                }
                await WaitUntil(() => hud.StoryOpen || hud.ReportOpen, 20);
                await Task.Delay(2600);
                await Capture(cam, dir, "24_ending", 0);
                guard = 0;
                while (!hud.ReportOpen && guard++ < 40) { hud.Advance(); await Task.Delay(400); }
                await Capture(cam, dir, "25_report", 1500);
                log.AppendLine($"결말 {s.EndingId} · 해결 {s.Solved} · {s.ClockLabel}");
                cam.targetTexture = null;
                log.AppendLine("OK");
            }
            catch (Exception e)
            {
                log.AppendLine("실패: " + e);
            }
            LastLog = log.ToString();
            File.WriteAllText(Path.Combine(dir, "smoke_log.txt"), LastLog);
            Debug.Log("[MysterySmoke]\n" + LastLog);
            Running = false;
        }

        static async Task Idle(MysteryGame game)
        {
            await Task.Delay(120);
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
