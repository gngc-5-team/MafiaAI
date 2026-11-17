using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Alibi.UI
{
    /// <summary>
    /// 상단 바(회중시계) · 쪽지 알림 · 컷신(모순! / 두 번째 비명 / 도주) · 모달(사건 파일 / 지목 / 사건 보고서).
    /// 전부 씬 저작. 코드는 글자 내용과 켜고 끄기, 그리고 기준 위치에서 잠깐 움직이는 연출만 한다.
    /// </summary>
    public class AlibiHud : MonoBehaviour
    {
        [Serializable]
        public class PickCard
        {
            public Button button;
            public Image art;
            public TMP_Text name;
            public Image deadMark;
        }

        [Header("상단 바")]
        public TMP_Text caseTitle;
        public TMP_Text caseFacts;
        public TMP_Text clockText;
        public TMP_Text threatText;
        public RectTransform hourHand;
        public RectTransform minuteHand;
        public TMP_Text aiStatus;
        public TMP_Text debugText;

        [Header("쪽지 알림")]
        public CanvasGroup toastGroup;
        public TMP_Text toastText;
        public float toastSeconds = 4f;

        [Header("컷신: 모순!")]
        public CanvasGroup objectionGroup;
        public RectTransform objectionRing;
        public RectTransform objectionWord;
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

        [Header("컷신: 도주")]
        public CanvasGroup deadlineGroup;
        public TMP_Text deadlineTitle;
        public Button deadlineButton;

        [Header("모달: 사건 파일")]
        public GameObject briefingRoot;
        public TMP_Text briefingText;
        public Button startButton;
        public TMP_Text startLabel;

        [Header("모달: 지목")]
        public GameObject accuseRoot;
        public PickCard[] pickCards = new PickCard[5];
        public Button accuseCancel;
        public GameObject confirmRoot;
        public TMP_Text confirmText;
        public Button confirmYes;
        public Button confirmNo;

        [Header("모달: 사건 보고서")]
        public GameObject resultRoot;
        public TMP_Text resultTitle;
        public RectTransform resultStamp;
        public TMP_Text resultStampWord;
        public TMP_Text resultText;
        public Button newCaseButton;

        [Header("표기(내용 형식)")]
        public string caseTitleFormat = "사건 №{0} — {1}, {2}에서 숨지다";
        public string caseFactsFormat = "23:15 비명 · 흉기 {0}({1}에 있던 것) · 23:45 하인이 발견";
        public string threatSecondFormat = "범인이 다시 움직이기까지 {0}분";
        public string threatSecondSoonFormat = "<color=#E0505F>범인이 다시 움직이기까지 {0}분</color>";
        public string threatDeadlineFormat = "<color=#E0505F>범인이 달아나기까지 {0}분</color>";
        public string threatNow = "<color=#E0505F>지금 지목하라</color>";
        public string startReady = "수사 시작";
        public string startWaiting = "AI 준비 중…";
        public string murderTitleText = "두 번째 비명";
        public string murderSubFormat = "{0}{1} {2}에서 숨진 채 발견됐다.";
        public string murderNoteFormat = "{0}의 수첩\n\n{1}";
        public string confirmFormat = "정말 <color=#9E1B30>{0}</color>{1}?";
        public string resultSolved = "사건 보고서 — 해결";
        public string resultUnsolved = "사건 보고서 — 미제";
        public string stampSolved = "해결";
        public string stampUnsolved = "미제";
        public Color stampSolvedColor = new Color(0.62f, 0.08f, 0.14f);
        public Color stampUnsolvedColor = new Color(0.2f, 0.18f, 0.2f);
        public int soonMinutes = 15;

        public event Action StartClicked, AccuseCancelled, NewCaseClicked;
        public event Action<int> PickClicked;
        public event Action<bool> ConfirmAnswered;

        Coroutine _toast;
        bool _clicked;
        bool _forcedAccuse;
        float _hourAngle, _minuteAngle;
        Coroutine _hands;

        void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(() => StartClicked?.Invoke());
            if (accuseCancel != null) accuseCancel.onClick.AddListener(() => { if (!_forcedAccuse) AccuseCancelled?.Invoke(); });
            if (newCaseButton != null) newCaseButton.onClick.AddListener(() => NewCaseClicked?.Invoke());
            if (confirmYes != null) confirmYes.onClick.AddListener(() => ConfirmAnswered?.Invoke(true));
            if (confirmNo != null) confirmNo.onClick.AddListener(() => ConfirmAnswered?.Invoke(false));
            for (int i = 0; i < pickCards.Length; i++)
            {
                int k = i;
                if (pickCards[i]?.button != null) pickCards[i].button.onClick.AddListener(() => PickClicked?.Invoke(k));
            }
            foreach (var b in new[] { objectionButton, murderButton, deadlineButton })
                if (b != null) b.onClick.AddListener(() => _clicked = true);
            HideGroup(toastGroup);
            HideGroup(objectionGroup);
            HideGroup(murderGroup);
            HideGroup(deadlineGroup);
            if (accuseRoot != null) accuseRoot.SetActive(false);
            if (resultRoot != null) resultRoot.SetActive(false);
        }

        static void HideGroup(CanvasGroup g)
        {
            if (g == null) return;
            g.alpha = 0f;
            g.blocksRaycasts = false;
            g.interactable = false;
            g.gameObject.SetActive(false);
        }

        static void ShowGroup(CanvasGroup g)
        {
            if (g == null) return;
            g.gameObject.SetActive(true);
            g.blocksRaycasts = true;
            g.interactable = true;
        }

        // ───────────── 상단 바 ─────────────

        public void SetTop(AlibiSession s)
        {
            var c = s.Case;
            if (caseTitle != null) caseTitle.text = string.Format(caseTitleFormat, c.Seed, c.Victim, c.MurderRoom);
            if (caseFacts != null) caseFacts.text = string.Format(caseFactsFormat, c.Weapon, c.WeaponRoom);
            if (clockText != null) clockText.text = s.ClockLabel;
            if (threatText != null)
            {
                int toSecond = s.MinutesToSecondMurder;
                threatText.text = s.DeadlineReached ? threatNow
                    : toSecond >= 0 ? string.Format(toSecond <= soonMinutes ? threatSecondSoonFormat : threatSecondFormat, toSecond)
                    : string.Format(threatDeadlineFormat, s.MinutesToDeadline);
            }
            SetHands(s.ClockLabel);
        }

        /// <summary>회중시계 바늘을 게임 시각으로 돌린다(시간이 흐르면 바늘이 미끄러지듯 따라간다).</summary>
        void SetHands(string hhmm)
        {
            var parts = hhmm.Split(':');
            int h = int.Parse(parts[0]), m = int.Parse(parts[1]);
            float minuteTarget = -m * 6f;
            float hourTarget = -((h % 12) * 30f + m * 0.5f);
            if (_hands != null) StopCoroutine(_hands);
            if (!isActiveAndEnabled) { ApplyHands(hourTarget, minuteTarget); return; }
            float h0 = _hourAngle, m0 = _minuteAngle;
            if (m0 - minuteTarget > 180f) m0 -= 360f;   // 59분→0분은 앞으로 돈다
            _hands = StartCoroutine(AlibiFx.Tween(0.6f, k => ApplyHands(Mathf.Lerp(h0, hourTarget, AlibiFx.EaseOutCubic(k)), Mathf.Lerp(m0, minuteTarget, AlibiFx.EaseOutCubic(k)))));
        }

        void ApplyHands(float hour, float minute)
        {
            _hourAngle = hour;
            _minuteAngle = minute;
            if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0, 0, hour);
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0, 0, minute);
        }

        public void SetAi(string text) { if (aiStatus != null) aiStatus.text = text; }
        public void SetDebug(string text) { if (debugText != null) debugText.text = text; }

        // ───────────── 쪽지 알림 ─────────────

        public void ShowToast(string text)
        {
            if (toastGroup == null) return;
            if (_toast != null) StopCoroutine(_toast);
            if (toastText != null) toastText.text = text;
            _toast = StartCoroutine(ToastRoutine());
        }

        IEnumerator ToastRoutine()
        {
            toastGroup.gameObject.SetActive(true);
            toastGroup.blocksRaycasts = false;
            yield return AlibiFx.Fade(toastGroup, 0f, 1f, 0.2f);
            yield return new WaitForSecondsRealtime(toastSeconds);
            yield return AlibiFx.Fade(toastGroup, 1f, 0f, 0.4f);
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

        /// <summary>「모순!」 — 붉은 먹 링이 쾅 찍히고 화면이 흔들린 뒤, 왜 양립할 수 없는지 한 줄이 올라온다.</summary>
        public IEnumerator PlayObjection(string explanation, RectTransform shakeTarget)
        {
            if (objectionGroup == null) yield break;
            if (objectionText != null) objectionText.text = explanation;
            ShowGroup(objectionGroup);
            if (objectionPanel != null) objectionPanel.gameObject.SetActive(false);
            yield return AlibiFx.Fade(objectionGroup, 0f, 1f, 0.12f);
            if (objectionRing != null) StartCoroutine(AlibiFx.Slam(objectionRing, 2.6f, 0.35f, 25f));
            if (objectionWord != null) yield return AlibiFx.Slam(objectionWord, 3.2f, 0.32f, -12f);
            if (shakeTarget != null) StartCoroutine(AlibiFx.Shake(shakeTarget, 18f, 0.35f));
            yield return new WaitForSecondsRealtime(0.35f);
            if (objectionPanel != null)
            {
                objectionPanel.gameObject.SetActive(true);
                yield return AlibiFx.SlideY(objectionPanel, -60f, 0.35f);
            }
            yield return WaitClick(9f);
            yield return AlibiFx.Fade(objectionGroup, 1f, 0f, 0.25f);
            HideGroup(objectionGroup);
        }

        /// <summary>두 번째 비명: 암전 → 핏자국 → 제목 → 희생자의 수첩 쪽지.</summary>
        public IEnumerator PlayMurder(SessionEvent ev)
        {
            if (murderGroup == null) yield break;
            if (murderTitle != null) murderTitle.text = murderTitleText;
            if (murderSub != null) murderSub.text = string.Format(murderSubFormat, ev.Victim, Josa.IGa(ev.Victim).Substring(ev.Victim.Length), ev.Room);
            if (murderNoteText != null) murderNoteText.text = string.Format(murderNoteFormat, ev.Victim, ev.Note);
            ShowGroup(murderGroup);
            if (murderNote != null) murderNote.gameObject.SetActive(false);
            if (murderSplatter != null) murderSplatter.localScale = Vector3.zero;
            yield return AlibiFx.Fade(murderGroup, 0f, 1f, 0.5f);
            yield return new WaitForSecondsRealtime(0.3f);
            if (murderSplatter != null) yield return AlibiFx.Slam(murderSplatter, 0.2f, 0.3f);
            yield return new WaitForSecondsRealtime(1.0f);
            if (murderNote != null)
            {
                murderNote.gameObject.SetActive(true);
                yield return AlibiFx.SlideY(murderNote, -120f, 0.5f);
            }
            yield return WaitClick(30f);
            yield return AlibiFx.Fade(murderGroup, 1f, 0f, 0.4f);
            HideGroup(murderGroup);
        }

        public IEnumerator PlayDeadline()
        {
            if (deadlineGroup == null) yield break;
            ShowGroup(deadlineGroup);
            yield return AlibiFx.Fade(deadlineGroup, 0f, 1f, 0.5f);
            if (deadlineTitle != null) yield return AlibiFx.Slam(deadlineTitle.rectTransform, 1.6f, 0.4f);
            yield return WaitClick(30f);
            yield return AlibiFx.Fade(deadlineGroup, 1f, 0f, 0.3f);
            HideGroup(deadlineGroup);
        }

        // ───────────── 모달 ─────────────

        public void ShowBriefing(string text, bool ready)
        {
            if (briefingRoot != null) briefingRoot.SetActive(true);
            if (briefingText != null) briefingText.text = text;
            SetStartReady(ready);
        }

        public void SetStartReady(bool ready)
        {
            if (startButton != null) startButton.interactable = ready;
            if (startLabel != null) startLabel.text = ready ? startReady : startWaiting;
        }

        public bool BriefingOpen => briefingRoot != null && briefingRoot.activeSelf;
        public void HideBriefing() { if (briefingRoot != null) briefingRoot.SetActive(false); }

        public bool AccuseOpen => accuseRoot != null && accuseRoot.activeSelf;
        public bool AccuseForced => _forcedAccuse;

        public void ShowAccuse(IReadOnlyList<string> names, IReadOnlyList<bool> dead, Func<string, Sprite> cardArt, bool forced)
        {
            if (accuseRoot == null) return;
            _forcedAccuse = forced;
            accuseRoot.SetActive(true);
            if (confirmRoot != null) confirmRoot.SetActive(false);
            if (accuseCancel != null) accuseCancel.gameObject.SetActive(!forced);
            for (int i = 0; i < pickCards.Length; i++)
            {
                var ui = pickCards[i];
                if (ui == null) continue;
                bool on = i < names.Count;
                if (ui.button != null) { ui.button.gameObject.SetActive(on); ui.button.interactable = on && !dead[i]; }
                if (!on) continue;
                if (ui.name != null) ui.name.text = names[i];
                if (ui.art != null) { ui.art.sprite = cardArt?.Invoke(names[i]); ui.art.color = dead[i] ? new Color(0.4f, 0.38f, 0.38f) : Color.white; }
                if (ui.deadMark != null) ui.deadMark.enabled = dead[i];
            }
        }

        public void AskConfirm(string name)
        {
            if (confirmRoot == null) return;
            confirmRoot.SetActive(true);
            if (confirmText != null) confirmText.text = string.Format(confirmFormat, name, "입니까");
        }

        public void HideConfirm() { if (confirmRoot != null) confirmRoot.SetActive(false); }

        public void HideAccuse()
        {
            _forcedAccuse = false;
            if (accuseRoot != null) accuseRoot.SetActive(false);
        }

        public IEnumerator ShowResult(bool solved, string text)
        {
            if (resultRoot == null) yield break;
            resultRoot.SetActive(true);
            if (resultTitle != null) resultTitle.text = solved ? resultSolved : resultUnsolved;
            if (resultText != null) resultText.text = text;
            if (resultStampWord != null)
            {
                resultStampWord.text = solved ? stampSolved : stampUnsolved;
                resultStampWord.color = solved ? stampSolvedColor : stampUnsolvedColor;
            }
            if (resultStamp != null)
            {
                resultStamp.gameObject.SetActive(false);
                yield return new WaitForSecondsRealtime(0.5f);
                resultStamp.gameObject.SetActive(true);
                yield return AlibiFx.Slam(resultStamp, 2.4f, 0.35f, 18f);
            }
        }

        public void HideResult() { if (resultRoot != null) resultRoot.SetActive(false); }
    }
}
