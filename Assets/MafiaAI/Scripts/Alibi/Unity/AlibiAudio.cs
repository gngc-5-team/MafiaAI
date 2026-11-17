using UnityEngine;

namespace MafiaAI.Alibi.UI
{
    /// <summary>배경음과 효과음. 잼 때 만든 사운드를 그대로 쓴다(클릭·확정·밤·역할 공개·엔딩).</summary>
    public class AlibiAudio : MonoBehaviour
    {
        public AudioSource music;
        public AudioSource sfx;
        public AudioClip musicClip;
        public AudioClip click;
        public AudioClip stamp;
        public AudioClip murder;
        public AudioClip reveal;
        public AudioClip ending;
        [Range(0f, 1f)] public float musicVolume = 0.28f;
        [Range(0f, 1f)] public float sfxVolume = 0.9f;

        void Start()
        {
            if (music == null || musicClip == null) return;
            music.clip = musicClip;
            music.loop = true;
            music.volume = musicVolume;
            music.Play();
        }

        void Play(AudioClip clip, float volume = 1f)
        {
            if (sfx != null && clip != null) sfx.PlayOneShot(clip, sfxVolume * volume);
        }

        public void Click() => Play(click, 0.55f);
        public void Stamp() => Play(stamp);
        public void Murder() => Play(murder);
        public void Reveal() => Play(reveal);
        public void Ending() => Play(ending);

        /// <summary>컷신 동안 배경음을 낮춘다.</summary>
        public void Duck(bool on)
        {
            if (music != null) music.volume = on ? musicVolume * 0.25f : musicVolume;
        }
    }
}
