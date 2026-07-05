using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 타이틀 화면 — UI는 TitleScene 하이라키에 저작되어 있고 이 스크립트는 참조+로직만.
    /// 타이틀이 떠 있는 동안 백그라운드로 로컬 AI(서버 기동 + 모델 예열)를 미리 준비해서,
    /// '시작'을 누르면 YuminScene이 즉시 워밍 상태로 시작되게 한다.
    /// 옵션: 해상도(◀▶ 순환)/전체화면 + 마스터/BGM/효과음 볼륨 — SettingsManager(PlayerPrefs)에 저장.
    /// </summary>
    public class TitleScreenUI : MonoBehaviour
    {
        [Header("씬 저작 UI 참조")]
        [SerializeField] Button startButton;
        [SerializeField] Button optionsButton;
        [SerializeField] GameObject optionsPanel;      // 기본 비활성
        [SerializeField] Button closeButton;
        [SerializeField] Button creditsButton;         // 크레딧 열기
        [SerializeField] CreditsRoll creditsRoll;      // 크레딧 롤(위로 흐르는 연출)
        [SerializeField] Button quitButton;            // 게임 종료
        [SerializeField] Button fullscreenButton;
        [SerializeField] TMP_Text fullscreenLabel;
        [SerializeField] Button resPrevButton;
        [SerializeField] Button resNextButton;
        [SerializeField] TMP_Text resLabel;
        [SerializeField] Slider masterSlider;
        [SerializeField] Slider bgmSlider;
        [SerializeField] Slider sfxSlider;
        [SerializeField] TMP_Text aiStatusText;        // 좌하단 "AI 준비 중…" 표시

        [Header("AI 리소스 셋업(동봉 실패 시 폴백)")]
        [SerializeField] GameObject setupPanel;        // 기본 비활성 — AI 준비 실패 시 표시
        [SerializeField] Button downloadButton;        // setup_ai.bat/.command 실행
        [SerializeField] Button recheckButton;         // 다시 확인
        [SerializeField] TMP_Text setupStatusText;     // 패널 안 상태 문구

        [Header("설정")]
        [SerializeField] string gameSceneName = "YuminScene";

        readonly List<Vector2Int> _resolutions = new();
        int _resIndex;
        static bool _aiWarm; // 씬 재방문 시 예열 반복 방지

        void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (optionsButton != null) optionsButton.onClick.AddListener(() => optionsPanel.SetActive(true));
            if (closeButton != null) closeButton.onClick.AddListener(() => { SettingsManager.Save(); optionsPanel.SetActive(false); });
            if (creditsButton != null && creditsRoll != null) creditsButton.onClick.AddListener(() => creditsRoll.Open());
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
            if (fullscreenButton != null) fullscreenButton.onClick.AddListener(ToggleFullscreen);
            if (resPrevButton != null) resPrevButton.onClick.AddListener(() => CycleResolution(-1));
            if (resNextButton != null) resNextButton.onClick.AddListener(() => CycleResolution(+1));
            if (masterSlider != null) masterSlider.onValueChanged.AddListener(v => SettingsManager.MasterVolume = v);
            if (bgmSlider != null) bgmSlider.onValueChanged.AddListener(v => SettingsManager.BgmVolume = v);
            if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(v => SettingsManager.SfxVolume = v);

            if (downloadButton != null) downloadButton.onClick.AddListener(RunSetupScript);
            if (recheckButton != null) recheckButton.onClick.AddListener(() => { _ = TryPrepareAi(); });

            if (optionsPanel != null) optionsPanel.SetActive(false);
            if (setupPanel != null) setupPanel.SetActive(false);
            BuildResolutionList();
            LoadUiFromSettings();
            SettingsManager.ApplyAudio();
        }

        void Start()
        {
            _ = TryPrepareAi();
        }

        /// <summary>AI 준비 시도: 설치된 Ollama → 동봉 엔진 순. 둘 다 실패하면 셋업 패널(다운로드/다시 확인) 표시.</summary>
        async System.Threading.Tasks.Task TryPrepareAi()
        {
            if (_aiWarm) { SetAiStatus("AI 준비 완료"); return; }

            SetAiStatus("AI 준비 중…");
            SetSetupStatus("AI 확인 중…");
            OllamaBootstrap.OnStatus += SetAiStatus;
            var cfg = new GameConfig(); // Model/BaseUrl 기본값 = 게임과 동일
            bool ok = await OllamaBootstrap.EnsureReadyAsync(cfg);
            if (ok)
            {
                try
                {
                    // 서버만 뜨면 모델은 아직 디스크에 있다 — 여기서 한 번 호출해 RAM에 올린다(진짜 오래 걸리는 부분).
                    SetAiStatus("AI 모델 로딩 중… (최초 1~2분)");
                    var client = new OllamaClient(cfg.BaseUrl);
                    await client.GenerateAsync(cfg.Model, "준비됐나?", "한 단어로만 답하라.", 0.1f, false, default, 4);
                    _aiWarm = true;
                    SetAiStatus("AI 준비 완료");
                    if (setupPanel != null) setupPanel.SetActive(false);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[TitleScreen] 모델 예열 실패: " + e.Message);
                    ShowSetupPanel("AI가 응답하지 않습니다. 아래 버튼으로 리소스를 설치해 주세요.");
                }
            }
            else ShowSetupPanel("AI 리소스가 없습니다. 아래 버튼으로 설치해 주세요. (인터넷 필요, 약 7.6GB)");
            OllamaBootstrap.OnStatus -= SetAiStatus;
        }

        void ShowSetupPanel(string msg)
        {
            SetAiStatus("AI 리소스 필요");
            SetSetupStatus(msg);
            if (setupPanel != null) setupPanel.SetActive(true);
        }

        /// <summary>플랫폼별 설치 스크립트를 눈에 보이는 콘솔로 실행(사용자가 진행 상황을 본다).</summary>
        void RunSetupScript()
        {
            try
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                string script = System.IO.Path.Combine(Application.streamingAssetsPath, "setup_ai.bat");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = script, UseShellExecute = true });
#else
                string script = System.IO.Path.Combine(Application.streamingAssetsPath, "setup_ai.command");
                try { System.Diagnostics.Process.Start("/bin/chmod", "+x \"" + script + "\""); } catch { }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = "/usr/bin/open", Arguments = "\"" + script + "\"", UseShellExecute = false });
#endif
                SetSetupStatus("설치 창이 열렸습니다. '완료!'가 뜨면 [다시 확인]을 누르세요.");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[TitleScreen] 설치 스크립트 실행 실패: " + e.Message);
                SetSetupStatus("실행 실패 — 게임 폴더의 StreamingAssets/setup_ai 파일을 직접 실행해 주세요.");
            }
        }

        void SetSetupStatus(string msg)
        {
            if (setupStatusText != null) setupStatusText.text = msg;
        }

        void OnDestroy() => OllamaBootstrap.OnStatus -= SetAiStatus;

        void StartGame() => SceneManager.LoadScene(gameSceneName);

        void QuitGame()
        {
#if UNITY_EDITOR
            // 에디터에선 Application.Quit이 안 먹으니 플레이 모드를 멈춘다.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- 옵션 ----------

        void BuildResolutionList()
        {
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x < 1024) continue;              // 너무 작은 건 제외
                if (!_resolutions.Contains(v)) _resolutions.Add(v);
            }
            if (_resolutions.Count == 0) _resolutions.Add(new Vector2Int(Screen.width, Screen.height));
            var cur = new Vector2Int(Screen.width, Screen.height);
            _resIndex = Mathf.Max(0, _resolutions.IndexOf(cur));
        }

        void CycleResolution(int dir)
        {
            _resIndex = (_resIndex + dir + _resolutions.Count) % _resolutions.Count;
            var r = _resolutions[_resIndex];
            SettingsManager.SetResolution(r.x, r.y);
            RefreshLabels();
        }

        void ToggleFullscreen()
        {
            SettingsManager.Fullscreen = !SettingsManager.Fullscreen;
            RefreshLabels();
        }

        void LoadUiFromSettings()
        {
            if (masterSlider != null) masterSlider.SetValueWithoutNotify(SettingsManager.MasterVolume);
            if (bgmSlider != null) bgmSlider.SetValueWithoutNotify(SettingsManager.BgmVolume);
            if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(SettingsManager.SfxVolume);
            RefreshLabels();
        }

        void RefreshLabels()
        {
            if (fullscreenLabel != null) fullscreenLabel.text = "전체화면: " + (SettingsManager.Fullscreen ? "켬" : "끔");
            if (resLabel != null && _resolutions.Count > 0)
            {
                var r = _resolutions[Mathf.Clamp(_resIndex, 0, _resolutions.Count - 1)];
                resLabel.text = r.x + " × " + r.y;
            }
        }

        void SetAiStatus(string msg)
        {
            if (aiStatusText != null) aiStatusText.text = msg;
        }
    }
}
