using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MafiaAI.Alibi.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MafiaAI.Alibi.EditorTools
{
    /// <summary>
    /// 실제 Play 모드 스모크 테스트. 두 가지로 쓴다.
    /// ① 배치모드(에디터가 꺼져 있을 때): Unity -batchmode -projectPath . -executeMethod MafiaAI.Alibi.EditorTools.AlibiSmokeTest.Run -alibiSmoke -alibiOut 폴더
    ///    (-quit 없이 — 끝나면 스스로 종료. Play 진입 시 도메인 리로드가 있어 InitializeOnLoad로 이어 받는다)
    /// ② 에디터가 열려 있을 때: Play 모드에서 AlibiSmokeTest.DriveInPlayMode(폴더)를 부르면 같은 흐름을 돌린다(MCP execute_code 등).
    /// 진행: 사건 파일 → 손님 목록 → 첫 대면 → 질문 쪽지 → 모순 제시(컷신) → 추궁 → 두 번째 비명 → 지목 → 보고서. 장면마다 PNG.
    /// </summary>
    [InitializeOnLoad]
    public static class AlibiSmokeTest
    {
        const string Flag = "-alibiSmoke";
        static RenderTexture _rt;
        public static string LastLog = "";
        public static bool Running;

        static AlibiSmokeTest()
        {
            if (!Environment.GetCommandLineArgs().Contains(Flag)) return;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode) DriveAndExit();
            };
        }

        public static void Run()
        {
            Directory.CreateDirectory(OutDir());
            EditorSceneManager.OpenScene(AlibiSceneBuilder.ScenePath);
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        static string OutDir()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-alibiOut");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Temp/AlibiSmoke");
        }

        static async void DriveAndExit()
        {
            bool ok = await Drive(OutDir(), true);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>에디터 Play 모드에서 직접 부른다(결과는 LastLog와 폴더의 PNG).</summary>
        public static async void DriveInPlayMode(string dir) => await DriveSeed(dir, 0);

        /// <summary>seed를 주면 그 사건으로 돌린다(직접형 사건이면 모순!·추궁 컷신까지 찍힌다).</summary>
        public static async System.Threading.Tasks.Task DriveSeed(string dir, int seed)
        {
            if (Running) return;
            _seed = seed;
            await Drive(dir, false);
        }

        static int _seed;

        static async Task<bool> Drive(string dir, bool ownRenderTarget)
        {
            Running = true;
            Application.runInBackground = true;   // 에디터 창이 포커스를 잃어도 프레임이 흐르게(안 그러면 코루틴 연출이 멈춘다)
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            bool ok = true;
            try
            {
                var game = await WaitFor(() => Object.FindFirstObjectByType<AlibiGame>(), 20);
                var cam = Camera.main;
                _rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                var prevTarget = cam.targetTexture;
                cam.targetTexture = _rt; // 캔버스가 1920×1080 기준으로 배치되게 고정

                await WaitUntil(() => game.hud.startButton.interactable, 150);
                if (_seed != 0) game.EditorNewCase(_seed);
                log.AppendLine("AI 상태: " + game.hud.aiStatus.text);
                await Capture(cam, dir, "01_briefing");
                game.EditorStart();
                await Capture(cam, dir, "02_lineup");

                var s = game.EditorSession;
                var c = s.Case;
                log.AppendLine($"사건 #{c.Seed} 피해자 {c.Victim}·{c.MurderRoom} 범인 {c.Killer.Name}({c.KillerLie}) 비밀 {c.SecretLiar.Name}({c.SecretLie}) {(c.ChainOnly ? "연쇄" : "직접")}");

                foreach (var p in c.Suspects)
                {
                    var sw = Stopwatch.StartNew();
                    game.EditorOpen(p.Name);
                    await Task.Delay(200);
                    await WaitUntil(() => !game.EditorBusy, 90);
                    log.AppendLine($"[첫 진술] {p.Name}: {s.Chats[p.Name].LastOrDefault().Text}  [{s.LastSource} {sw.ElapsedMilliseconds}ms]{(s.LastProblem != null ? " ⚠ " + s.LastProblem : "")}");
                    if (p == c.Suspects[0]) await Capture(cam, dir, "03_first_testimony", 1600);
                }

                // 질문 쪽지로 '누구 봤어?' 몇 개
                foreach (var p in c.Suspects.Take(3))
                {
                    game.EditorOpen(p.Name);
                    await WaitUntil(() => !game.EditorBusy, 30);
                    var chip = s.QuickQuestions(p.Name).FirstOrDefault();
                    if (chip.Question == null || !s.CanAct) continue;
                    var sw = Stopwatch.StartNew();
                    game.EditorAsk(chip.Question, true);
                    await Task.Delay(200);
                    await WaitUntil(() => !game.EditorBusy, 90);
                    log.AppendLine($"탐정→{p.Name}: {chip.Question}\n   {s.Chats[p.Name].LastOrDefault().Text}  [{s.LastSource} {sw.ElapsedMilliseconds}ms]{(s.LastProblem != null ? " ⚠ " + s.LastProblem : "")}");
                }
                await Capture(cam, dir, "04_chip_answer", 1600);

                // 모순 찾아 제시 → 컷신 → 추궁
                var pair = FindPair(s);
                if (pair != null && s.CanAct)
                {
                    game.EditorSelect(pair.Value.A, pair.Value.B);
                    await Capture(cam, dir, "05_selected");
                    game.EditorPresent();
                    await Task.Delay(1500);
                    await Capture(cam, dir, "06_objection", 0);
                    game.hud.objectionButton.onClick.Invoke();
                    await WaitUntil(() => !game.EditorBusy, 20);
                    var rec = s.Contradictions.LastOrDefault();
                    log.AppendLine("모순 제시 → " + (rec != null ? rec.Explanation : "실패"));
                    if (rec != null)
                        foreach (var person in new[] { rec.A.Person, rec.B.Person }.Distinct())
                        {
                            if (!s.CanPress(person, rec)) continue;
                            game.EditorOpen(person);
                            await WaitUntil(() => !game.EditorBusy, 30);
                            game.EditorPress(rec.Id);
                            await Task.Delay(200);
                            await WaitUntil(() => !game.EditorBusy, 90);
                            log.AppendLine($"  추궁 {person} → {rec.Reactions[person]}: {s.Chats[person].Last().Text}  [{s.LastSource}]");
                            await Capture(cam, dir, "07_press_" + rec.Reactions[person], 1600);
                        }
                }
                else log.AppendLine("드러난 진술만으로는 아직 모순 쌍이 없음");

                // 시계를 흘려 두 번째 비명까지
                int guard = 0;
                while (!s.SecondMurderDone && s.CanAct && guard++ < 20)
                {
                    var p = c.Suspects.FirstOrDefault(x => !x.Dead && s.QuickQuestions(x.Name).Count > 0);
                    if (p == null) break;
                    game.EditorOpen(p.Name);
                    await WaitUntil(() => !game.EditorBusy, 30);
                    game.EditorAsk(s.QuickQuestions(p.Name)[0].Question, true);
                    await Task.Delay(150);
                    await WaitUntil(() => !game.EditorBusy || game.hud.murderGroup.gameObject.activeSelf, 90);
                    if (game.hud.murderGroup.gameObject.activeSelf)
                    {
                        await Task.Delay(3200);
                        await Capture(cam, dir, "08_second_murder", 0);
                        game.hud.murderButton.onClick.Invoke();
                    }
                    await WaitUntil(() => !game.EditorBusy, 90);
                }
                log.AppendLine($"두 번째 희생자: {s.SecondVictim ?? "(없음)"} · 시각 {s.ClockLabel}");
                await Capture(cam, dir, "09_after_murder");

                game.notebook.accuseButton.onClick.Invoke();
                await Capture(cam, dir, "10_accuse");
                game.EditorAccuse(c.Killer.Name);
                await Task.Delay(1500);
                await Capture(cam, dir, "11_result", 0);
                log.AppendLine("지목 결과: " + (s.Result != null && s.Result.Correct ? "정답" : "오답"));
                cam.targetTexture = prevTarget;
                log.AppendLine("OK");
            }
            catch (Exception e)
            {
                log.AppendLine("실패: " + e);
                ok = false;
            }
            LastLog = log.ToString();
            File.WriteAllText(Path.Combine(dir, "smoke_log.txt"), LastLog);
            Debug.Log("[AlibiSmoke]\n" + LastLog);
            Running = false;
            return ok;
        }

        static (CellRef A, CellRef B)? FindPair(AlibiSession s)
        {
            var c = s.Case;
            var known = c.Suspects.SelectMany(p => Enumerable.Range(0, c.SlotCount).Select(t => new CellRef(p.Name, t))).Where(s.IsKnown).ToList();
            for (int i = 0; i < known.Count; i++)
                for (int j = i + 1; j < known.Count; j++)
                    if (ContradictionRules.Check(s.View(known[i]), s.View(known[j])).Valid
                        && !s.Contradictions.Any(r => r.Touches(known[i].Person, known[i].Slot) && r.Touches(known[j].Person, known[j].Slot)))
                        return (known[i], known[j]);
            return null;
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
