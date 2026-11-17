using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MafiaAI.Alibi.UI;

namespace MafiaAI.Mystery.UI
{
    /// <summary>
    /// 상단 바(회중시계·새벽까지) · 쪽지 알림(줄 세워 하나씩) · 컷신(모순! / 두 번째 비명) · 이야기 장면 · 사건 보고서.
    /// 전부 씬 저작. 코드는 글자 내용과 켜고 끄기, 기준 위치에서 잠깐 움직이는 연출만 한다.
    /// </summary>
    public class MysteryHud : MonoBehaviour
    {
        [Header("상단 바")]
        public TMP_Text caseTitle;
        public TMP_Text caseSub;
        public TMP_Text clockText;
        public TMP_Text dawnText;
        public TMP_Text aiStatus;
        public RectTransform hourHand;
        public RectTransform minuteHand;

        [Header("쪽지 알림")]
        public CanvasGroup toastGroup;
        public TMP_Text toastText;
        public float toastSeconds = 2.6f;

        [Header("컷신: 모순!")]
        public CanvasGroup objectionGroup;
        public RectTransform objectionRing;
        public RectTransform objectionWord;
        public TMP_Text objectionWordText;
        public RectTransform objectionPanel;
        public TMP_Text objectionText;
        public Button objectionButton;

        [Header("컷신: 두 번째 비명")]
        public CanvasGroup murderGroup;
        public RectTransform murderSplatter;
        public TMP_Text murderTitle;
        public TMP_Text murderSub;
        public RectTransform murderNote;
        public TMP_Text murderNoteText;
        public Button murderButton;

        [Header("이야기 장면")]
        public CanvasGroup storyGroup;
        public Image storyImage;
        public TMP_Text storyText;
        public TMP_Text storyTitle;
        public Button storyButton;
        public AlibiTypewriter storyTypewriter;

        [Header("사건 보고서")]
        public GameObject reportRoot;
        public TMP_Text reportTitle;
        public TMP_Text reportText;
        public RectTransform reportStamp;
        public TMP_Text reportStampWord;
        public Button restartButton;

        [Header("표기")]
        public string dawnFormat = "새벽까지 {0}시간 {1:00}분";
        public string dawnSoonFormat = "<color=#E0505F>새벽까지 {0}분</color>";
        public string objectionWordDefault = "모순!";
        public string stampSolved = "해결";
        public string stampUnsolved = "미제";
        public Color stampSolvedColor = new Color(0.62f, 0.08f, 0.14f);
        public Color stampUnsolvedColor = new Color(0.2f, 0.18f, 0.2f);

        public event Action RestartClicked;

        readonly Queue<string> _toasts = new Queue<string>();
        Coroutine _toast;
        bool _clicked;
        float _hourAngle, _minuteAngle;
        Coroutine _hands;
        Coroutine _zoom;

        void Awake()
        {
            foreach (var b in new[] { objectionButton, murderButton, storyButton })
                if (b != null) b.onClick.AddListener(() => _clicked = true);
            if (restartButton != null) restartButton.onClick.AddListener(() => RestartClicked?.Invoke());
            Hide(toastGroup);
            Hide(objectionGroup);
            Hide(murderGroup);
            Hide(storyGroup);
            if (reportRoot != null) reportRoot.SetActive(false);
        }

        static void Hide(CanvasGroup g)
        {
            if (g == null) return;
            g.alpha = 0f;
            g.blocksRaycasts = false;
            g.interactable = false;
            g.gameObject.SetActive(false);
        }

        static void Show(CanvasGroup g)
        {
            if (g == null) return;
            g.gameObject.SetActive(true);
            g.blocksRaycasts = true;
            g.interactable = true;
        }

        public bool StoryOpen => storyGroup != null && storyGroup.gameObject.activeSelf;
        public bool ReportOpen => reportRoot != null && reportRoot.activeSelf;

        // ───────────── 상단 바 ─────────────

        public void SetTop(MysterySession s)
        {
            if (caseTitle != null) caseTitle.text = s.Case.Title;
            if (caseSub != null) caseSub.text = s.Case.Subtitle;
            if (clockText != null) clockText.text = s.ClockLabel;
            if (dawnText != null)
            {
                int left = s.MinutesToDawn;
                dawnText.text = left <= 60 ? string.Format(dawnSoonFormat, left) : string.Format(dawnFormat, left / 60, left % 60);
            }
            SetHands(s.ClockLabel);
        }

        public void SetAi(string text) { if (aiStatus != null) aiStatus.text = text; }

        void SetHands(string hhmm)
        {
            var parts = hhmm.Split(':');
            int h = int.Parse(parts[0]), m = int.Parse(parts[1]);
            float minuteTarget = -m * 6f;
            float hourTarget = -((h % 12) * 30f + m * 0.5f);
            if (_hands != null) StopCoroutine(_hands);
            if (!isActiveAndEnabled) { ApplyHands(hourTarget, minuteTarget); return; }
            float h0 = _hourAngle, m0 = _minuteAngle;
            while (m0 - minuteTarget > 180f) m0 -= 360f;
            while (h0 - hourTarget > 180f) h0 -= 360f;
            _hands = StartCoroutine(AlibiFx.Tween(0.7f, k => ApplyHands(Mathf.Lerp(h0, hourTarget, AlibiFx.EaseOutCubic(k)), Mathf.Lerp(m0, minuteTarget, AlibiFx.EaseOutCubic(k)))));
        }

        void ApplyHands(float hour, float minute)
        {
            _hourAngle = hour;
            _minuteAngle = minute;
            if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0, 0, hour);
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0, 0, minute);
        }

        // ───────────── 쪽지 알림(줄 세워 하나씩) ─────────────

        public void Toast(string text)
        {
            if (toastGroup == null || string.IsNullOrEmpty(text)) return;
            while (_toasts.Count >= 2) _toasts.Dequeue();   // 밀린 알림은 버린다 — 지금 일이 늦게 뜨면 더 헷갈린다
            _toasts.Enqueue(text);
            if (_toast == null) _toast = StartCoroutine(ToastLoop());
        }

        IEnumerator ToastLoop()
        {
            while (_toasts.Count > 0)
            {
                if (toastText != null) toastText.text = _toasts.Dequeue();
                toastGroup.gameObject.SetActive(true);
                toastGroup.blocksRaycasts = false;
                yield return AlibiFx.Fade(toastGroup, 0f, 1f, 0.18f);
                yield return new WaitForSecondsRealtime(_toasts.Count > 0 ? toastSeconds * 0.55f : toastSeconds);
                yield return AlibiFx.Fade(toastGroup, 1f, 0f, 0.3f);
            }
            toastGroup.gameObject.SetActive(false);
            _toast = null;
        }

        // ───────────── 컷신 ─────────────

        IEnumerator WaitClick(float maxSeconds)
        {
            _clicked = false;
            float t = 0f;
            while (!_clicked && t < maxSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>「모순!」 — 붉은 먹 링이 쾅 찍히고 화면이 흔들린 뒤, 왜 무너졌는지 한 줄이 올라온다.</summary>
        public IEnumerator PlayObjection(string word, string explanation, RectTransform shakeTarget)
        {
            if (objectionGroup == null) yield break;
            if (objectionWordText != null) objectionWordText.text = string.IsNullOrEmpty(word) ? objectionWordDefault : word;
            if (objectionText != null) objectionText.text = explanation ?? "";
            Show(objectionGroup);
            if (objectionPanel != null) objectionPanel.gameObject.SetActive(false);
            yield return AlibiFx.Fade(objectionGroup, 0f, 1f, 0.12f);
            if (objectionRing != null) StartCoroutine(AlibiFx.Slam(objectionRing, 2.6f, 0.35f, 25f));
            if (objectionWord != null) yield return AlibiFx.Slam(objectionWord, 3.2f, 0.32f, -12f);
            if (shakeTarget != null) StartCoroutine(AlibiFx.Shake(shakeTarget, 18f, 0.35f));
            yield return new WaitForSecondsRealtime(0.35f);
            if (objectionPanel != null && !string.IsNullOrEmpty(explanation))
            {
                objectionPanel.gameObject.SetActive(true);
                yield return AlibiFx.SlideY(objectionPanel, -60f, 0.35f);
            }
            yield return WaitClick(12f);
            yield return AlibiFx.Fade(objectionGroup, 1f, 0f, 0.25f);
            Hide(objectionGroup);
        }

        /// <summary>두 번째 비명: 암전 → 핏자국 → 제목 → 쪽지.</summary>
        public IEnumerator PlayMurder(string title, string sub, string note)
        {
            if (murderGroup == null) yield break;
            if (murderTitle != null) murderTitle.text = title;
            if (murderSub != null) murderSub.text = sub;
            if (murderNoteText != null) murderNoteText.text = note;
            Show(murderGroup);
            if (murderNote != null) murderNote.gameObject.SetActive(false);
            if (murderSplatter != null) murderSplatter.localScale = Vector3.zero;
            yield return AlibiFx.Fade(murderGroup, 0f, 1f, 0.5f);
            yield return new WaitForSecondsRealtime(0.3f);
            if (murderSplatter != null) yield return AlibiFx.Slam(murderSplatter, 0.2f, 0.3f);
            yield return new WaitForSecondsRealtime(1.0f);
            if (murderNote != null && !string.IsNullOrEmpty(note))
            {
                murderNote.gameObject.SetActive(true);
                yield return AlibiFx.SlideY(murderNote, -120f, 0.5f);
            }
            yield return WaitClick(30f);
            yield return AlibiFx.Fade(murderGroup, 1f, 0f, 0.4f);
            Hide(murderGroup);
        }

        /// <summary>이야기 장면(프롤로그·엔딩): 그림이 서서히 밝아지고 글이 타자기로 흐른다. 클릭으로 넘긴다.</summary>
        public IEnumerator PlayStory(IReadOnlyList<Panel> panels, Func<string, Sprite> art, string title = null)
        {
            if (storyGroup == null || panels == null) yield break;
            Show(storyGroup);
            storyGroup.alpha = 1f;
            if (storyTitle != null) { storyTitle.text = title ?? ""; storyTitle.gameObject.SetActive(!string.IsNullOrEmpty(title)); }
            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (storyImage != null)
                {
                    var sp = art?.Invoke(p.Image);
                    storyImage.sprite = sp;
                    storyImage.enabled = sp != null;
                    storyImage.color = new Color(1, 1, 1, 0);
                    StartCoroutine(AlibiFx.Tween(0.9f, k => storyImage.color = new Color(1, 1, 1, k)));
                    var rt = storyImage.rectTransform;
                    if (_zoom != null) StopCoroutine(_zoom);
                    _zoom = StartCoroutine(AlibiFx.Tween(12f, k => rt.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.05f, k)));   // 천천히 다가가는 카메라
                }
                if (storyTypewriter != null) storyTypewriter.Play(p.Text);
                else if (storyText != null) storyText.text = p.Text;
                yield return new WaitForSecondsRealtime(0.25f);
                _clicked = false;
                while (!_clicked) yield return null;
                if (storyTypewriter != null && storyTypewriter.IsPlaying)
                {
                    storyTypewriter.Complete();
                    _clicked = false;
                    while (!_clicked) yield return null;
                }
            }
            yield return AlibiFx.Fade(storyGroup, 1f, 0f, 0.5f);
            Hide(storyGroup);
        }

        /// <summary>엔딩 장면의 클릭 하나를 대신 누른다(자동 검증용).</summary>
        public void Advance() => _clicked = true;

        public IEnumerator ShowReport(bool solved, string title, string text)
        {
            if (reportRoot == null) yield break;
            reportRoot.SetActive(true);
            if (reportTitle != null) reportTitle.text = title;
            if (reportText != null) reportText.text = text;
            if (reportStampWord != null)
            {
                reportStampWord.text = solved ? stampSolved : stampUnsolved;
                reportStampWord.color = solved ? stampSolvedColor : stampUnsolvedColor;
            }
            if (reportStamp != null)
            {
                reportStamp.gameObject.SetActive(false);
                yield return new WaitForSecondsRealtime(0.5f);
                reportStamp.gameObject.SetActive(true);
                yield return AlibiFx.Slam(reportStamp, 2.4f, 0.35f, 18f);
            }
        }

        public void HideReport() { if (reportRoot != null) reportRoot.SetActive(false); }
    }
}
