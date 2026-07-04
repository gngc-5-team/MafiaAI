using UnityEngine;

namespace MafiaAI.UI
{
    /// <summary>
    /// 게임 설정(사운드/디스플레이) — PlayerPrefs로 저장되어 씬과 세션을 넘어 유지된다.
    /// 타이틀 옵션 패널이 쓰고, GameAudioController가 Awake에서 읽어 적용한다.
    /// </summary>
    public static class SettingsManager
    {
        const string KeyMaster = "opt_master";
        const string KeyBgm = "opt_bgm";
        const string KeySfx = "opt_sfx";
        const string KeyFullscreen = "opt_fullscreen";
        const string KeyResW = "opt_res_w";
        const string KeyResH = "opt_res_h";

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(KeyMaster, 1f);
            set { PlayerPrefs.SetFloat(KeyMaster, Mathf.Clamp01(value)); ApplyAudio(); }
        }

        /// <summary>BGM 배율(1=디자이너 기본 볼륨 그대로).</summary>
        public static float BgmVolume
        {
            get => PlayerPrefs.GetFloat(KeyBgm, 1f);
            set => PlayerPrefs.SetFloat(KeyBgm, Mathf.Clamp01(value));
        }

        /// <summary>효과음(발소리 포함) 배율.</summary>
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(KeySfx, 1f);
            set => PlayerPrefs.SetFloat(KeySfx, Mathf.Clamp01(value));
        }

        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            set { PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0); ApplyDisplay(); }
        }

        public static void SetResolution(int w, int h)
        {
            PlayerPrefs.SetInt(KeyResW, w);
            PlayerPrefs.SetInt(KeyResH, h);
            ApplyDisplay();
        }

        /// <summary>마스터 볼륨을 전역(AudioListener)에 적용. 어느 씬에서든 호출 가능.</summary>
        public static void ApplyAudio() => AudioListener.volume = MasterVolume;

        /// <summary>저장된 해상도/전체화면을 적용(저장값 없으면 현재 유지).</summary>
        public static void ApplyDisplay()
        {
            int w = PlayerPrefs.GetInt(KeyResW, Screen.width);
            int h = PlayerPrefs.GetInt(KeyResH, Screen.height);
            Screen.SetResolution(w, h, Fullscreen);
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
