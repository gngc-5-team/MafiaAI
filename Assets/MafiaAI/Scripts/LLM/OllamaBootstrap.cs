using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;

namespace MafiaAI.LLM
{
    /// <summary>
    /// 심사위원 배포용 원클릭 부트스트랩: 외부 설치/터미널 없이 게임이 스스로 LLM을 준비한다.
    /// 1) 기존 Ollama(config.BaseUrl)가 살아있고 모델이 있으면 그대로 사용 (개발자 머신).
    /// 2) 아니면 StreamingAssets/ollama/ 에 동봉된 Ollama 바이너리를 전용 포트(11435)로 자식 프로세스 기동,
    ///    동봉 모델 폴더(OLLAMA_MODELS)를 물려서 즉시 사용. 게임 종료 시 프로세스도 종료.
    /// 빌드 전 tools/package_llm.sh 로 바이너리+모델을 StreamingAssets에 채워 넣어야 한다.
    /// </summary>
    public static class OllamaBootstrap
    {
        const string BundledHost = "http://127.0.0.1:11435";

        static Process _proc;
        static bool _quitHooked;

        /// <summary>진행 상황(로딩 문구 표시용). 구독 안 해도 동작엔 지장 없음.</summary>
        public static event Action<string> OnStatus;

        /// <summary>
        /// LLM 사용 준비를 보장한다. 성공 시 config.BaseUrl이 실제 사용할 서버로 조정될 수 있다.
        /// </summary>
        public static async Task<bool> EnsureReadyAsync(GameConfig config)
        {
            string model = config.Model;

            // 1) 이미 떠 있는 서버(개발 머신의 기본 Ollama 등)에 모델이 있으면 그대로 사용
            Status("로컬 AI 확인 중…");
            if (await ServerHasModel(config.BaseUrl, model, 2f))
            {
                Status("기존 로컬 AI 사용: " + config.BaseUrl);
                return true;
            }

            // 2) 이전 실행이 남긴 동봉 서버가 살아있으면 재사용
            if (await ServerHasModel(BundledHost, model, 2f))
            {
                config.BaseUrl = BundledHost;
                Status("동봉 AI 서버 재사용");
                return true;
            }

            // 3) 동봉 바이너리 기동
            string root = Path.Combine(Application.streamingAssetsPath, "ollama");
            string exe = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? Path.Combine(root, "win", "ollama.exe")
                : Path.Combine(root, "mac", "ollama");
            string models = Path.Combine(root, "models");

            if (!File.Exists(exe) || !Directory.Exists(models))
            {
                Debug.LogError("[OllamaBootstrap] 동봉 AI가 없습니다: " + exe +
                               "\n빌드 전 tools/package_llm.sh 를 실행해 StreamingAssets를 채우세요.");
                Status("AI 파일 없음 — 실행 불가");
                return false;
            }

            // macOS: zip 배포 과정에서 실행 권한이 날아갔을 수 있으니 복구
            if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
                TryChmodX(exe);

            Status("AI 엔진 시작 중…");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "serve",
                    WorkingDirectory = Path.GetDirectoryName(exe), // 윈도우는 exe 옆 lib/ 러너 필요
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.EnvironmentVariables["OLLAMA_HOST"] = "127.0.0.1:11435";
                psi.EnvironmentVariables["OLLAMA_MODELS"] = models;

                _proc = Process.Start(psi);
                // 파이프가 가득 차 멈추지 않게 출력은 계속 비운다
                _proc.OutputDataReceived += (_, __) => { };
                _proc.ErrorDataReceived += (_, __) => { };
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
                HookQuit();
            }
            catch (Exception e)
            {
                Debug.LogError("[OllamaBootstrap] AI 엔진 기동 실패: " + e.Message);
                Status("AI 엔진 기동 실패");
                return false;
            }

            // 4) 서버가 뜰 때까지 폴링 (최대 30초)
            for (int i = 0; i < 60; i++)
            {
                if (_proc.HasExited)
                {
                    Debug.LogError("[OllamaBootstrap] AI 엔진이 종료됨 (exit " + _proc.ExitCode + ")");
                    Status("AI 엔진 오류");
                    return false;
                }
                if (await ServerHasModel(BundledHost, model, 1.5f))
                {
                    config.BaseUrl = BundledHost;
                    Status("AI 준비 완료");
                    Debug.Log("[OllamaBootstrap] 동봉 AI 기동 완료: " + BundledHost + " (" + model + ")");
                    return true;
                }
                await Task.Delay(500);
            }

            Debug.LogError("[OllamaBootstrap] AI 서버 응답 대기 시간 초과");
            Status("AI 준비 시간 초과");
            return false;
        }

        /// <summary>해당 주소의 Ollama가 응답하고, 모델 태그가 목록에 있는지.</summary>
        static async Task<bool> ServerHasModel(string baseUrl, string model, float timeoutSec)
        {
            try
            {
                using var www = UnityWebRequest.Get(baseUrl.TrimEnd('/') + "/api/tags");
                www.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSec));
                var op = www.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (www.result != UnityWebRequest.Result.Success) return false;
                string body = www.downloadHandler.text ?? "";
                return body.Contains("\"" + model + "\"");
            }
            catch { return false; }
        }

        static void TryChmodX(string path)
        {
            try
            {
                using var p = Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/chmod", Arguments = "+x \"" + path + "\"",
                    UseShellExecute = false, CreateNoWindow = true
                });
                p?.WaitForExit(3000);
            }
            catch { /* 권한이 이미 있으면 무시 */ }
        }

        static void HookQuit()
        {
            if (_quitHooked) return;
            _quitHooked = true;
            Application.quitting += () =>
            {
                try
                {
                    if (_proc != null && !_proc.HasExited)
                    {
                        _proc.Kill();
                        _proc.WaitForExit(2000);
                    }
                }
                catch { /* 이미 죽었으면 무시 */ }
            };
        }

        static void Status(string msg)
        {
            Debug.Log("[OllamaBootstrap] " + msg);
            OnStatus?.Invoke(msg);
        }
    }
}
