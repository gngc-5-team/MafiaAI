using System.Collections;
using TMPro;
using UnityEngine;

namespace MafiaAI.Alibi.UI
{
    /// <summary>
    /// 한 글자씩 흘리는 대사(타자기). 생각 중일 땐 '……'이 숨 쉬듯 깜빡인다 — LLM 대기 1~2초를 '망설임' 연기로 바꾼다.
    /// 글자 내용만 바꾸고 서식은 오브젝트 그대로 둔다.
    /// </summary>
    public class AlibiTypewriter : MonoBehaviour
    {
        public TMP_Text target;
        public float charsPerSecond = 42f;
        public string thinkingText = "……";

        Coroutine _co;
        public bool IsPlaying { get; private set; }

        public void Play(string text)
        {
            if (target == null) return;
            Stop();
            _co = StartCoroutine(Run(text ?? ""));
        }

        public void Show(string text)
        {
            Stop();
            if (target == null) return;
            target.text = text ?? "";
            target.maxVisibleCharacters = int.MaxValue;
        }

        public void Thinking()
        {
            Stop();
            _co = StartCoroutine(Dots());
        }

        public void Complete()
        {
            if (!IsPlaying || target == null) return;
            Stop();
            target.maxVisibleCharacters = int.MaxValue;
        }

        void Stop()
        {
            if (_co != null) StopCoroutine(_co);
            _co = null;
            IsPlaying = false;
        }

        IEnumerator Run(string text)
        {
            IsPlaying = true;
            target.text = text;
            target.maxVisibleCharacters = 0;
            target.ForceMeshUpdate();
            int total = target.textInfo.characterCount;
            float shown = 0f;
            while (shown < total)
            {
                shown += charsPerSecond * Time.unscaledDeltaTime;
                target.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
                yield return null;
            }
            target.maxVisibleCharacters = int.MaxValue;
            IsPlaying = false;
        }

        IEnumerator Dots()
        {
            target.maxVisibleCharacters = int.MaxValue;
            float t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime;
                int n = 1 + (int)(t * 2.5f) % thinkingText.Length;
                target.text = thinkingText.Substring(0, Mathf.Min(thinkingText.Length, n));
                yield return null;
            }
        }
    }
}
