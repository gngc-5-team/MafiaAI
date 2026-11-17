using UnityEngine;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>등대 빛: 회전하는 빛줄기를 옆에서 본 것처럼 — 길이가 늘었다 줄며 좌우로 넘어가고, 정면을 비출 때 등이 번쩍인다.</summary>
    public class LighthouseBeam : MonoBehaviour
    {
        public Transform beam;            // 등 위치에 피벗을 둔 빛줄기(오른쪽을 향해 그린 것)
        public SpriteRenderer beamSprite;
        public SpriteRenderer lampGlow;
        public float period = 9f;
        public float beamAlpha = 0.5f;
        float _t;

        void Update()
        {
            _t += Time.deltaTime;
            float phase = _t / period * Mathf.PI * 2f;
            float dir = Mathf.Cos(phase);
            if (beam != null)
            {
                float len = Mathf.Abs(dir);
                beam.localScale = new Vector3(Mathf.Sign(dir) * Mathf.Max(0.02f, len), 1f, 1f);
            }
            if (beamSprite != null)
            {
                var col = beamSprite.color;
                beamSprite.color = new Color(col.r, col.g, col.b, beamAlpha * Mathf.SmoothStep(0f, 1f, Mathf.Abs(dir)));
            }
            if (lampGlow != null)
            {
                float facing = Mathf.Pow(1f - Mathf.Abs(dir), 6f);            // 빛이 우리 쪽을 향하는 순간
                float back = Mathf.Sin(phase) > 0f ? 1f : 0.4f;               // 뒤쪽을 비출 땐 어둡다
                var col = lampGlow.color;
                lampGlow.color = new Color(col.r, col.g, col.b, Mathf.Clamp01(0.35f + facing * 0.9f * back));
            }
        }
    }
}
