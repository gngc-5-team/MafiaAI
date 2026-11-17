using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 번개: 하늘과 창문이 하얗게 번쩍이고, 저택 전체 조명이 잠깐 차갑게 치솟는다. 잠시 뒤 천둥.
    /// 일정하지 않은 간격으로 저절로 치고, 컷신에서 직접 부를 수도 있다.
    /// </summary>
    public class LightningController : MonoBehaviour
    {
        public Light2D globalLight;
        public SpriteRenderer sky;
        public SpriteRenderer[] windowGlows;
        public Light2D[] windowLights;
        public AudioSource thunderSource;
        public AudioClip[] thunder;
        public ManorCamera cameraRig;
        public Vector2 interval = new Vector2(14f, 32f);
        public Color flashColor = new Color(0.78f, 0.86f, 1f);
        public bool auto = true;

        [Header("번개 줄기(하늘 겹)")]
        public SpriteRenderer[] bolts;          // 미리 깔아 둔 번개 그림들(평소엔 꺼짐)
        public Camera viewCamera;               // 줄기를 지금 보이는 하늘 어딘가에 친다
        public Rect houseRect;                  // 집이 가리는 곳 — 줄기는 되도록 집 밖으로 보이는 하늘에
        public SpriteRenderer[] flashRenderers; // 번쩍일 때 함께 밝아지는 구름·안개·바다 겹
        Color[] _flashBase;
        Coroutine _strike;                      // 한 번에 한 번개만 — 겹치면 앞 것을 끄고 새로 친다
        SpriteRenderer _strikeBolt;

        float _next;
        float _globalBase;
        Color _globalColor;
        Color _skyBase;
        float[] _winBase;

        void Start()
        {
            if (globalLight != null) { _globalBase = globalLight.intensity; _globalColor = globalLight.color; }
            if (sky != null) _skyBase = sky.color;
            if (windowLights != null)
            {
                _winBase = new float[windowLights.Length];
                for (int i = 0; i < windowLights.Length; i++) if (windowLights[i] != null) _winBase[i] = windowLights[i].intensity;
            }
            _next = Time.time + Random.Range(4f, 9f);
            if (bolts != null) foreach (var b in bolts) if (b != null) b.enabled = false;
            if (flashRenderers != null)
            {
                _flashBase = new Color[flashRenderers.Length];
                for (int i = 0; i < flashRenderers.Length; i++)
                    if (flashRenderers[i] != null) _flashBase[i] = flashRenderers[i].color;
            }
        }

        void Update()
        {
            if (!auto || Time.time < _next) return;
            _next = Time.time + Random.Range(interval.x, interval.y);
            FlashNow(Random.Range(0.55f, 1f));
        }

        public void FlashNow(float strength = 1f)
        {
            if (_strike != null)
            {
                StopCoroutine(_strike);
                Apply(0f, _strikeBolt);
            }
            _strike = StartCoroutine(Strike(strength));
        }

        /// <summary>이번 번개가 아직 하늘에 그려져 있는지(점검용).</summary>
        public bool BoltVisible => _strikeBolt != null && _strikeBolt.enabled;

        IEnumerator Strike(float strength)
        {
            // 하늘 어딘가에 줄기 하나(이 번개만의 것 — 겹친 번개가 서로의 줄기를 켜고 끄지 않게)
            SpriteRenderer bolt = null;
            if (bolts != null && bolts.Length > 0 && strength > 0.5f)
            {
                bolt = bolts[Random.Range(0, bolts.Length)];
                if (bolt != null && viewCamera != null)
                {
                    var layer = bolt.transform.parent;
                    var world = new Vector3(BoltX(), viewCamera.transform.position.y, 0f);
                    var local = layer != null ? layer.InverseTransformPoint(world) : world;
                    bolt.transform.localPosition = new Vector3(local.x, bolt.transform.localPosition.y, 0f);
                    bolt.flipX = Random.value > 0.5f;
                }
            }
            _strikeBolt = bolt;
            // 두 번 끊어 치는 번쩍임
            yield return Pulse(strength, 0.06f, 0.12f, bolt);
            yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
            yield return Pulse(strength * 0.8f, 0.05f, 0.55f, bolt);
            _strikeBolt = null;
            if (thunderSource != null && thunder != null && thunder.Length > 0)
            {
                yield return new WaitForSeconds(Random.Range(0.4f, 1.6f));
                thunderSource.pitch = Random.Range(0.85f, 1.05f);
                thunderSource.PlayOneShot(thunder[Random.Range(0, thunder.Length)], Mathf.Lerp(0.45f, 0.9f, strength));
                if (cameraRig != null) cameraRig.Shake(0.05f * strength, 0.5f);
            }
            _strike = null;
        }

        /// <summary>지금 화면에서 집에 가리지 않는 하늘 쪽(왼쪽·오른쪽 바깥)의 x — 둘 다 없으면 아무 데나.</summary>
        float BoltX()
        {
            var c = viewCamera.transform.position;
            float halfW = viewCamera.orthographicSize * viewCamera.aspect;
            float l0 = c.x - halfW * 0.92f, r1 = c.x + halfW * 0.92f;
            float l1 = Mathf.Min(r1, houseRect.xMin - 0.8f), r0 = Mathf.Max(l0, houseRect.xMax + 0.8f);
            float left = Mathf.Max(0f, l1 - l0), right = Mathf.Max(0f, r1 - r0);
            if (houseRect.width <= 0f || left + right < 0.5f) return Random.Range(l0, r1);
            float pick = Random.Range(0f, left + right);
            return pick < left ? l0 + pick : r0 + (pick - left);
        }

        IEnumerator Pulse(float strength, float up, float down, SpriteRenderer bolt)
        {
            float t = 0f;
            while (t < up + down)
            {
                t += Time.deltaTime;
                float k = t < up ? t / up : 1f - (t - up) / down;
                k = Mathf.Clamp01(k) * strength;
                Apply(k, bolt);
                yield return null;
            }
            Apply(0f, bolt);
        }

        void Apply(float k, SpriteRenderer bolt)
        {
            if (bolt != null)
            {
                bolt.enabled = k > 0.02f;
                var bc = bolt.color;
                bolt.color = new Color(bc.r, bc.g, bc.b, Mathf.Clamp01(k * 1.4f));
            }
            if (flashRenderers != null && _flashBase != null)
                for (int i = 0; i < flashRenderers.Length; i++)
                {
                    var r = flashRenderers[i];
                    if (r == null) continue;
                    var b = _flashBase[i];
                    r.color = Color.Lerp(b, new Color(0.85f, 0.92f, 1f, Mathf.Min(1f, b.a * 1.6f + 0.1f)), k * 0.8f);
                }
            if (globalLight != null)
            {
                globalLight.intensity = _globalBase + k * 1.1f;
                globalLight.color = Color.Lerp(_globalColor, flashColor, k);
            }
            // 하늘은 반쯤만 밝힌다 — 다 하얗게 날리면 번개 줄기가 묻힌다
            if (sky != null) sky.color = Color.Lerp(_skyBase, new Color(1f, 1f, 1f), k * 0.55f);
            if (windowGlows != null)
                foreach (var g in windowGlows)
                    if (g != null) { var c = g.color; g.color = new Color(c.r, c.g, c.b, k * 0.9f); }
            if (windowLights != null)
                for (int i = 0; i < windowLights.Length; i++)
                    if (windowLights[i] != null) windowLights[i].intensity = _winBase[i] + k * 2.2f;
        }
    }
}
