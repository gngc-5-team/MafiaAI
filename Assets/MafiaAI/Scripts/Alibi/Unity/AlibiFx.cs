using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Alibi.UI
{
    /// <summary>
    /// 연출용 작은 트윈 모음(외부 트윈 라이브러리 없이 코루틴으로). 전부 unscaled time — 컷신 중 시간이 멈춰도 돈다.
    /// 위치·크기의 '기준값'은 씬에 저작된 값이고, 트윈은 그 기준에서 잠깐 흔들거나 키웠다가 반드시 원래대로 돌려놓는다.
    /// </summary>
    public static class AlibiFx
    {
        public static float EaseOutBack(float t) { const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2); }
        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3);
        public static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3) / 2f;

        public static IEnumerator Tween(float duration, Action<float> step)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                step(Mathf.Clamp01(t / duration));
                yield return null;
            }
            step(1f);
        }

        public static IEnumerator Fade(CanvasGroup cg, float from, float to, float duration)
        {
            if (cg == null) yield break;
            yield return Tween(duration, k => cg.alpha = Mathf.Lerp(from, to, EaseOutCubic(k)));
        }

        /// <summary>도장 찍듯: 크게 → 원래 크기로 튕기며 들어온다.</summary>
        public static IEnumerator Slam(RectTransform rt, float fromScale, float duration, float fromAngle = 0f)
        {
            if (rt == null) yield break;
            var baseScale = Vector3.one;
            float baseAngle = rt.localEulerAngles.z;
            yield return Tween(duration, k =>
            {
                float e = EaseOutBack(k);
                rt.localScale = baseScale * Mathf.LerpUnclamped(fromScale, 1f, e);
                rt.localRotation = Quaternion.Euler(0, 0, baseAngle + Mathf.LerpUnclamped(fromAngle, 0f, e));
            });
        }

        /// <summary>제자리에서 흔든다(끝나면 정확히 원위치).</summary>
        public static IEnumerator Shake(RectTransform rt, float amplitude, float duration)
        {
            if (rt == null) yield break;
            var origin = rt.anchoredPosition;
            yield return Tween(duration, k =>
            {
                float a = amplitude * (1f - k);
                rt.anchoredPosition = origin + new Vector2(UnityEngine.Random.Range(-a, a), UnityEngine.Random.Range(-a * 0.6f, a * 0.6f));
            });
            rt.anchoredPosition = origin;
        }

        public static IEnumerator ScaleTo(RectTransform rt, float from, float to, float duration)
        {
            if (rt == null) yield break;
            yield return Tween(duration, k => rt.localScale = Vector3.one * Mathf.Lerp(from, to, EaseInOut(k)));
        }

        /// <summary>색을 확 바꿨다가 원래 색으로 돌아온다(붉은 섬광 등).</summary>
        public static IEnumerator Flash(Graphic g, Color peak, float duration)
        {
            if (g == null) yield break;
            var baseColor = g.color;
            yield return Tween(duration, k =>
            {
                float w = k < 0.15f ? k / 0.15f : 1f - (k - 0.15f) / 0.85f;
                g.color = Color.Lerp(baseColor, peak, w);
            });
            g.color = baseColor;
        }

        public static IEnumerator SlideY(RectTransform rt, float offset, float duration)
        {
            if (rt == null) yield break;
            var target = rt.anchoredPosition;
            var from = target + new Vector2(0, offset);
            yield return Tween(duration, k => rt.anchoredPosition = Vector2.LerpUnclamped(from, target, EaseOutBack(k)));
        }
    }
}
