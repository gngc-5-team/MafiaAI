using UnityEngine;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 그 자리에서 나는 소리(벽난로 타닥임 · 괘종 째깍 · 주전자 김 · 지하 물방울).
    /// loop면 시작 지점을 무작위로 골라 계속 틀고, 아니면 clips 중 하나를 띄엄띄엄 낸다.
    /// AudioSource는 3D — 귀(AudioListener)가 저택 평면 위에 있어 다가가면 커지고 좌우로 갈린다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SoundEmitter : MonoBehaviour
    {
        public bool loop = true;
        public AudioClip[] clips;
        public Vector2 interval = new Vector2(1.2f, 3.5f);
        public Vector2 pitch = new Vector2(0.95f, 1.05f);
        [Range(0f, 1f)] public float volume = 0.5f;

        AudioSource _src;
        float _next;

        void Start()
        {
            _src = GetComponent<AudioSource>();
            if (loop && _src.clip != null)
            {
                _src.loop = true;
                _src.volume = volume;
                _src.time = Random.Range(0f, _src.clip.length * 0.9f);
                _src.Play();
            }
            _next = Time.time + Random.Range(interval.x, interval.y);
        }

        void Update()
        {
            if (loop || clips == null || clips.Length == 0 || Time.time < _next) return;
            _next = Time.time + Random.Range(interval.x, interval.y);
            _src.pitch = Random.Range(pitch.x, pitch.y);
            _src.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
        }
    }
}
