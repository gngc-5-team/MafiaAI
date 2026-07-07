using UnityEngine;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 게임 사운드 총괄 — GameController 이벤트에만 구독해서 동작한다(다른 UI 코드 무침범).
    /// 클립과 볼륨은 전부 SerializeField(인스펙터에서 교체/조절).
    /// 매핑: bgm=상시 루프 · role_assigned=역할 배정(게임 셋업) · night_start=밤 진입 ·
    ///       dawn_birds=새벽 진입 · vote_select=표 1건 집계마다 · vote_confirm=처형 확정/무산 ·
    ///       game_end=승패 결정 · footstep=플레이어 이동 중 루프.
    /// </summary>
    public class GameAudioController : MonoBehaviour
    {
        [Header("게임 연결")]
        [SerializeField] GameController controller;
        [SerializeField] SpriteMansionView mansionView;

        [Header("클립")]
        [SerializeField] AudioClip bgm;
        [SerializeField] AudioClip roleAssigned;
        [SerializeField] AudioClip nightStart;
        [SerializeField] AudioClip dawnBirds;
        [SerializeField] AudioClip voteSelect;
        [SerializeField] AudioClip voteConfirm;
        [SerializeField] AudioClip gameEnd;
        [SerializeField] AudioClip footstep;

        [Header("볼륨")]
        [Range(0f, 1f)][SerializeField] float bgmVolume = 0.30f;
        [Range(0f, 1f)][SerializeField] float sfxVolume = 0.85f;
        [Range(0f, 1f)][SerializeField] float footstepVolume = 0.5f;
        [Tooltip("밤에는 BGM을 이 배율로 낮춰 긴장감을 준다")]
        [Range(0f, 1f)][SerializeField] float bgmNightDuck = 0.45f;
        [Tooltip("이 속도(유닛/초) 이상 움직이면 발소리 재생")]
        [SerializeField] float footstepSpeedThreshold = 0.6f;

        AudioSource _bgmSrc;   // 루프 배경음
        AudioSource _sfxSrc;   // 원샷 효과음
        AudioSource _stepSrc;  // 발소리 루프
        Phase _lastPhase = Phase.End;
        Vector3 _lastHumanPos;
        bool _hasLastPos;

        void Awake()
        {
            _inst = this;
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (mansionView == null) mansionView = FindFirstObjectByType<SpriteMansionView>();

            // 옵션(타이틀 화면) 설정 반영: 마스터=전역, BGM/효과음=디자이너 기본값에 배율
            SettingsManager.ApplyAudio();
            bgmVolume *= SettingsManager.BgmVolume;
            sfxVolume *= SettingsManager.SfxVolume;
            footstepVolume *= SettingsManager.SfxVolume;

            _bgmSrc = gameObject.AddComponent<AudioSource>();
            _bgmSrc.loop = true; _bgmSrc.playOnAwake = false; _bgmSrc.clip = bgm; _bgmSrc.volume = bgmVolume;

            _sfxSrc = gameObject.AddComponent<AudioSource>();
            _sfxSrc.loop = false; _sfxSrc.playOnAwake = false; _sfxSrc.volume = sfxVolume;

            _stepSrc = gameObject.AddComponent<AudioSource>();
            _stepSrc.loop = true; _stepSrc.playOnAwake = false; _stepSrc.clip = footstep; _stepSrc.volume = footstepVolume;

            if (bgm != null) _bgmSrc.Play();
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnGameSetup += HandleSetup;
            controller.OnPhaseChanged += HandlePhase;
            controller.OnVoteCast += HandleVoteCast;
            controller.OnGameEnd += HandleGameEnd;
            controller.OnLog += HandleLog;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnGameSetup -= HandleSetup;
            controller.OnPhaseChanged -= HandlePhase;
            controller.OnVoteCast -= HandleVoteCast;
            controller.OnGameEnd -= HandleGameEnd;
            controller.OnLog -= HandleLog;
        }

        void HandleSetup() => PlayOneShot(roleAssigned);           // 처음 직업(역할) 배정

        void HandlePhase(GameState s)
        {
            if (s == null || s.Phase == _lastPhase) return;
            _lastPhase = s.Phase;

            switch (s.Phase)
            {
                case Phase.Night: PlayOneShot(nightStart); break;  // 밤소리
                case Phase.Dawn:  PlayOneShot(dawnBirds);  break;  // 아침 새소리
            }

            // 밤엔 BGM을 낮춰 어둡게
            if (_bgmSrc != null)
                _bgmSrc.volume = s.Phase == Phase.Night ? bgmVolume * bgmNightDuck : bgmVolume;
        }

        // 표 아이콘이 생길 때마다(누군가의 표가 집계될 때마다) 확정 소리
        void HandleVoteCast() => PlayOneShot(voteConfirm);

        void HandleLog(LogEntry e) { } // (구독 유지용 — 현재 로그 기반 사운드 없음)

        static GameAudioController _inst;

        /// <summary>투표 UI에서 후보 카드를 클릭(선택)했을 때 — ChoiceOverlayUI가 호출.</summary>
        public static void PlayVoteSelectClick()
        {
            if (_inst != null) _inst.PlayOneShot(_inst.voteSelect);
        }

        void HandleGameEnd(Winner w) => PlayOneShot(gameEnd);      // 승패 결정

        void Update()
        {
            UpdateFootsteps();
        }

        // 플레이어가 실제로 움직이는 동안만 발소리 루프
        void UpdateFootsteps()
        {
            if (_stepSrc == null || footstep == null || mansionView == null || controller == null) return;

            bool canWalk = controller.HumanPlayer != null && controller.HumanPlayer.Alive &&
                           controller.State != null &&
                           (controller.State.Phase == Phase.Discuss || controller.State.Phase == Phase.Night);

            var pos = mansionView.HumanWorldPosition;
            if (!_hasLastPos) { _lastHumanPos = pos; _hasLastPos = true; return; }

            float speed = (pos - _lastHumanPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            _lastHumanPos = pos;

            bool moving = canWalk && speed > footstepSpeedThreshold;
            if (moving && !_stepSrc.isPlaying) _stepSrc.Play();
            else if (!moving && _stepSrc.isPlaying) _stepSrc.Stop();
        }

        void PlayOneShot(AudioClip clip)
        {
            if (clip != null && _sfxSrc != null) _sfxSrc.PlayOneShot(clip, sfxVolume);
        }
    }
}
