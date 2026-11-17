using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>촛불·벽난로가 숨 쉬듯 일렁인다(조명 세기와 빛 번짐 그림을 함께).</summary>
    public class LightFlicker : MonoBehaviour
    {
        public Light2D light2D;
        public SpriteRenderer glow;
        public float amount = 0.18f;
        public float speed = 5f;
        float _base;
        float _glowBase;
        float _seed;

        void Awake()
        {
            _seed = Random.value * 100f;
            if (light2D != null) _base = light2D.intensity;
            if (glow != null) _glowBase = glow.color.a;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(_seed, Time.time * speed) - 0.5f;
            float k = 1f + amount * n * 2f;
            if (light2D != null) light2D.intensity = _base * k;
            if (glow != null) { var c = glow.color; glow.color = new Color(c.r, c.g, c.b, _glowBase * k); }
        }
    }
}
