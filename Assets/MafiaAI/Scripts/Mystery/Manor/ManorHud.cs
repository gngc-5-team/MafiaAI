using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MafiaAI.Alibi.UI;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 시네마 HUD: 위아래 레터박스 안에만 정보가 산다(가운데 화면은 비워 둔다).
    /// 위 — 회중시계·시각·새벽까지 / 목표. 아래 — 자막(말하는 이 + 대사) · 지금 방 · 조작 안내 · 단면 미니맵.
    /// 물건 앞에 서면 작은 마름모 표식이 뜬다. 장(章) 제목 카드와 암전도 여기서.
    /// </summary>
    public class ManorHud : MonoBehaviour
    {
        [Header("레터박스")]
        public RectTransform topBar;
        public RectTransform bottomBar;
        public float topNormal = 112f, topCinema = 150f;
        public float bottomNormal = 150f, bottomCinema = 196f;

        [Header("위")]
        public TMP_Text clockText;
        public TMP_Text dawnText;
        public RectTransform hourHand;
        public RectTransform minuteHand;
        public TMP_Text objectiveText;
        public CanvasGroup topGroup;

        [Header("아래")]
        public TMP_Text speakerText;
        public TMP_Text subtitleText;
        public AlibiTypewriter typewriter;
        public TMP_Text roomText;
        public TMP_Text hintText;
        public Image[] miniCells;          // 단면 미니맵(행 우선 3×3, 왼쪽 아래 칸은 바위)
        public Image[] miniPeople;         // 미니맵 위 손님 점(필요한 만큼)
        public CanvasGroup bottomGroup;

        [Header("표식")]
        public RectTransform prompt;
        public TMP_Text promptLabel;
        public TMP_Text promptKey;
        public Image promptDiamond;

        [Header("사진(살펴보기) — 탐정이 들여다본 그 물건")]
        public CanvasGroup photoGroup;
        public RectTransform photoRect;
        public Image photoImage;
        public Image photoFlash;
        public TMP_Text photoCaption;
        public TMP_Text photoStamp;
        public float photoTilt = -2.5f;

        [Header("돋보기 표식(E) — 이 방에서 살펴볼 수 있는 것")]
        public RectTransform markerRoot;
        public RectTransform markerTemplate;     // 꺼 둔 원본 하나 — 필요한 만큼 복제해 쓴다
        public Sprite markerLook;
        public Sprite markerStairUp;
        public Sprite markerStairDown;
        public Color markerNew = new Color(1f, 0.86f, 0.55f, 1f);
        public Color markerDone = new Color(0.62f, 0.6f, 0.58f, 0.75f);

        [Header("알림")]
        public CanvasGroup toastGroup;
        public TMP_Text toastText;
        public float toastSeconds = 3f;

        [Header("장 제목 · 암전")]
        public CanvasGroup chapterGroup;
        public TMP_Text chapterTitle;
        public TMP_Text chapterSub;
        public CanvasGroup fadeGroup;

        [Header("색")]
        public Color cellHere = new Color(0.91f, 0.69f, 0.44f, 1f);
        public Color cellVisited = new Color(0.55f, 0.5f, 0.45f, 0.9f);
        public Color cellUnknown = new Color(0.25f, 0.23f, 0.24f, 0.9f);
        public Color promptDone = new Color(0.62f, 0.58f, 0.52f, 1f);
        public Color promptNew = new Color(0.91f, 0.69f, 0.44f, 1f);

        readonly Queue<string> _toasts = new Queue<string>();
        readonly List<RectTransform> _markers = new List<RectTransform>();
        Coroutine _toast, _bars, _narrate, _hands, _photo;
        Vector2 _photoHome;
        bool _photoShown;
        bool _photoRight;
        float _hourAngle, _minuteAngle;
        Canvas _canvas;

        void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (toastGroup != null) { toastGroup.alpha = 0f; toastGroup.gameObject.SetActive(false); }
            if (chapterGroup != null) { chapterGroup.alpha = 0f; chapterGroup.gameObject.SetActive(false); }
            HidePrompt();
            HideSubtitle();
            if (photoRect != null) _photoHome = photoRect.anchoredPosition;
            if (photoGroup != null) { photoGroup.alpha = 0f; photoGroup.gameObject.SetActive(false); }
            if (markerTemplate != null) markerTemplate.gameObject.SetActive(false);
        }

        // ───────────── 위 ─────────────

        public void SetClock(MysterySession s)
        {
            if (clockText != null) clockText.text = s.ClockLabel;
            if (dawnText != null)
            {
                int left = s.MinutesToDawn;
                dawnText.text = left <= 60 ? $"<color=#E0505F>새벽까지 {left}분</color>" : $"새벽까지 {left / 60}시간 {left % 60:00}분";
            }
            var parts = s.ClockLabel.Split(':');
            int h = int.Parse(parts[0]), m = int.Parse(parts[1]);
            float mt = -m * 6f, ht = -((h % 12) * 30f + m * 0.5f);
            if (_hands != null) StopCoroutine(_hands);
            float h0 = _hourAngle, m0 = _minuteAngle;
            while (m0 - mt > 180f) m0 -= 360f;
            while (h0 - ht > 180f) h0 -= 360f;
            if (!isActiveAndEnabled) { ApplyHands(ht, mt); return; }
            _hands = StartCoroutine(AlibiFx.Tween(0.6f, k => ApplyHands(Mathf.Lerp(h0, ht, AlibiFx.EaseOutCubic(k)), Mathf.Lerp(m0, mt, AlibiFx.EaseOutCubic(k)))));
        }

        void ApplyHands(float hour, float minute)
        {
            _hourAngle = hour;
            _minuteAngle = minute;
            if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0, 0, hour);
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0, 0, minute);
        }

        public void SetObjective(string text)
        {
            if (objectiveText != null) objectiveText.text = string.IsNullOrEmpty(text) ? "" : "◆  " + text;
        }

        // ───────────── 아래 ─────────────

        /// <summary>지금 있는 방(shown을 주면 연출상 보이는 방 — 최후의 추궁처럼 모두를 한 방에 모을 때).</summary>
        public void SetRoom(MysterySession s, string shown = null)
        {
            var c = s.Case;
            string here = shown ?? s.Room;
            if (roomText != null) roomText.text = $"{c.FloorLabel(here)}   {c.RoomById(here).Name}";
            if (miniCells == null) return;
            foreach (var r in c.Rooms)
            {
                int i = r.Y * 3 + r.X;
                if (i < 0 || i >= miniCells.Length || miniCells[i] == null) continue;
                miniCells[i].color = r.Id == here ? cellHere : s.Visited(r.Id) ? cellVisited : cellUnknown;
            }
        }

        public void SetHints(string text) { if (hintText != null) hintText.text = text; }

        /// <summary>자막 한 줄(말하는 이가 비면 서술).</summary>
        public void Say(string speaker, string text, bool type = true)
        {
            if (_narrate != null) { StopCoroutine(_narrate); _narrate = null; }
            if (speakerText != null) speakerText.text = string.IsNullOrEmpty(speaker) ? "" : speaker;
            if (subtitleText != null) subtitleText.fontStyle = string.IsNullOrEmpty(speaker) ? FontStyles.Italic : FontStyles.Normal;
            if (typewriter != null && type) typewriter.Play(text ?? "");
            else if (typewriter != null) typewriter.Show(text ?? "");
            else if (subtitleText != null) subtitleText.text = text ?? "";
        }

        /// <summary>서술 자막을 띄웠다가 읽을 만큼 지나면 지운다.</summary>
        public void Narrate(string text, float hold = -1f)
        {
            Say(null, text);
            if (hold < 0f) hold = 2.4f + (text?.Length ?? 0) * 0.055f;
            _narrate = StartCoroutine(HideAfter(hold));
        }

        IEnumerator HideAfter(float s)
        {
            yield return new WaitForSecondsRealtime(s);
            HideSubtitle();
            _narrate = null;
        }

        public bool Typing => typewriter != null && typewriter.IsPlaying;
        public void CompleteTyping() { if (typewriter != null) typewriter.Complete(); }

        public void HideSubtitle()
        {
            if (speakerText != null) speakerText.text = "";
            if (typewriter != null) typewriter.Show("");
            else if (subtitleText != null) subtitleText.text = "";
        }

        // ───────────── 표식 ─────────────

        public void ShowPrompt(Camera cam, Vector3 world, string label, string key, bool done)
        {
            if (prompt == null || _canvas == null) return;
            prompt.gameObject.SetActive(true);
            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform, screen, _canvas.worldCamera, out var local);
            prompt.localPosition = new Vector3(local.x, local.y, 0f);
            if (promptLabel != null) { promptLabel.text = label; promptLabel.color = done ? promptDone : Color.white; }
            if (promptKey != null) promptKey.text = key;
            if (promptDiamond != null) promptDiamond.color = done ? promptDone : promptNew;
        }

        public void HidePrompt() { if (prompt != null) prompt.gameObject.SetActive(false); }

        // ───────────── 사진 ─────────────

        public bool PhotoShown => _photoShown;

        /// <summary>살펴본 물건의 사진을 띄운다(셔터 플래시 → 미끄러져 들어와 살짝 기운다). rightSide면 오른쪽에 — 물건·탐정을 가리지 않게.</summary>
        public void ShowPhoto(Sprite sprite, string caption, string stamp, bool rightSide = false)
        {
            if (photoGroup == null || photoImage == null || sprite == null) { HidePhoto(); return; }
            _photoRight = rightSide;
            if (photoRect != null)
            {
                photoRect.anchorMin = photoRect.anchorMax = new Vector2(rightSide ? 1f : 0f, 0.5f);
            }
            photoImage.sprite = sprite;
            if (photoCaption != null) photoCaption.text = caption ?? "";
            if (photoStamp != null) photoStamp.text = stamp ?? "";
            if (_photo != null) StopCoroutine(_photo);
            photoGroup.gameObject.SetActive(true);
            _photoShown = true;
            _photo = StartCoroutine(PhotoIn());
        }

        IEnumerator PhotoIn()
        {
            float a0 = photoGroup.alpha;
            if (photoFlash != null) photoFlash.color = new Color(1f, 1f, 1f, 0.95f);
            float side = _photoRight ? -1f : 1f;
            var home = new Vector2(Mathf.Abs(_photoHome.x) * side, _photoHome.y);
            float tilt = photoTilt * side;
            yield return AlibiFx.Tween(0.38f, k =>
            {
                float e = AlibiFx.EaseOutCubic(k);
                photoGroup.alpha = Mathf.Lerp(a0, 1f, Mathf.Clamp01(k * 2.2f));
                if (photoRect != null)
                {
                    photoRect.anchoredPosition = home + new Vector2(Mathf.Lerp(-140f * side, 0f, e), Mathf.Lerp(-24f, 0f, e));
                    photoRect.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(tilt - 7f * side, tilt, e));
                }
                if (photoFlash != null) photoFlash.color = new Color(1f, 1f, 1f, 0.95f * (1f - Mathf.Clamp01(k * 1.6f)));
            });
            _photo = null;
        }

        public void HidePhoto()
        {
            if (photoGroup == null || !_photoShown) return;
            _photoShown = false;
            if (_photo != null) StopCoroutine(_photo);
            if (!isActiveAndEnabled) { photoGroup.alpha = 0f; photoGroup.gameObject.SetActive(false); return; }
            _photo = StartCoroutine(PhotoOut());
        }

        IEnumerator PhotoOut()
        {
            float a0 = photoGroup.alpha;
            var p0 = photoRect != null ? photoRect.anchoredPosition : Vector2.zero;
            yield return AlibiFx.Tween(0.25f, k =>
            {
                photoGroup.alpha = Mathf.Lerp(a0, 0f, k);
                if (photoRect != null) photoRect.anchoredPosition = p0 + new Vector2(-90f * k * k * (_photoRight ? -1f : 1f), 0f);
            });
            photoGroup.gameObject.SetActive(false);
            _photo = null;
        }

        // ───────────── 돋보기 표식 ─────────────

        /// <summary>kind: 0 살펴보기 · 1 위층 계단 · 2 아래층 계단. done이면 이미 살펴본 것(흐리게).</summary>
        public struct Marker { public Vector3 world; public int kind; public bool done; }

        public void SetMarkers(Camera cam, IList<Marker> list)
        {
            if (markerRoot == null || markerTemplate == null || _canvas == null) return;
            while (_markers.Count < list.Count)
            {
                var m = Instantiate(markerTemplate, markerRoot);
                m.name = "Marker" + _markers.Count;
                _markers.Add(m);
            }
            float t = Time.unscaledTime;
            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                bool on = i < list.Count;
                if (m.gameObject.activeSelf != on) m.gameObject.SetActive(on);
                if (!on) continue;
                var mk = list[i];
                var screen = RectTransformUtility.WorldToScreenPoint(cam, mk.world);
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform, screen, _canvas.worldCamera, out var local);
                float bob = mk.done ? 0f : Mathf.Round(Mathf.Sin(t * 3.2f + i * 1.7f) * 3f);
                m.localPosition = new Vector3(Mathf.Round(local.x), Mathf.Round(local.y) + bob, 0f);
                var icon = m.Find("Icon");
                var img = icon != null ? icon.GetComponent<Image>() : null;
                if (img != null)
                {
                    img.sprite = mk.kind == 1 ? markerStairUp : mk.kind == 2 ? markerStairDown : markerLook;
                    var c = mk.done ? markerDone : markerNew;
                    if (!mk.done && mk.kind == 0) c.a *= 0.8f + 0.2f * Mathf.Sin(t * 4f + i);
                    img.color = c;
                }
            }
        }

        public void HideMarkers()
        {
            foreach (var m in _markers) if (m != null && m.gameObject.activeSelf) m.gameObject.SetActive(false);
        }

        // ───────────── 알림 ─────────────

        public void Toast(string text)
        {
            if (toastGroup == null || string.IsNullOrEmpty(text)) return;
            while (_toasts.Count >= 2) _toasts.Dequeue();
            _toasts.Enqueue(text);
            if (_toast == null) _toast = StartCoroutine(ToastLoop());
        }

        IEnumerator ToastLoop()
        {
            while (_toasts.Count > 0)
            {
                if (toastText != null) toastText.text = _toasts.Dequeue();
                toastGroup.gameObject.SetActive(true);
                yield return AlibiFx.Fade(toastGroup, 0f, 1f, 0.2f);
                yield return new WaitForSecondsRealtime(_toasts.Count > 0 ? toastSeconds * 0.6f : toastSeconds);
                yield return AlibiFx.Fade(toastGroup, 1f, 0f, 0.35f);
            }
            toastGroup.gameObject.SetActive(false);
            _toast = null;
        }

        // ───────────── 레터박스 · 장 제목 · 암전 ─────────────

        /// <summary>0 = 평소, 1 = 영화(대화·컷신 — 띠가 두꺼워진다).</summary>
        public void SetCinema(float amount, float seconds = 0.5f)
        {
            if (_bars != null) StopCoroutine(_bars);
            float t0 = topBar != null ? topBar.sizeDelta.y : topNormal;
            float b0 = bottomBar != null ? bottomBar.sizeDelta.y : bottomNormal;
            float t1 = Mathf.Lerp(topNormal, topCinema, amount), b1 = Mathf.Lerp(bottomNormal, bottomCinema, amount);
            if (!isActiveAndEnabled || seconds <= 0f) { ApplyBars(t1, b1); return; }
            _bars = StartCoroutine(AlibiFx.Tween(seconds, k => ApplyBars(Mathf.Lerp(t0, t1, AlibiFx.EaseInOut(k)), Mathf.Lerp(b0, b1, AlibiFx.EaseInOut(k)))));
        }

        void ApplyBars(float top, float bottom)
        {
            if (topBar != null) topBar.sizeDelta = new Vector2(topBar.sizeDelta.x, top);
            if (bottomBar != null) bottomBar.sizeDelta = new Vector2(bottomBar.sizeDelta.x, bottom);
        }

        public void ShowChrome(bool on)
        {
            if (topGroup != null) topGroup.alpha = on ? 1f : 0f;
            if (bottomGroup != null) bottomGroup.alpha = on ? 1f : 0f;
        }

        public IEnumerator Chapter(string title, string sub, float hold = 2.2f)
        {
            if (chapterGroup == null) yield break;
            if (chapterTitle != null) chapterTitle.text = title;
            if (chapterSub != null) chapterSub.text = sub ?? "";
            chapterGroup.gameObject.SetActive(true);
            chapterGroup.blocksRaycasts = true;
            yield return AlibiFx.Fade(chapterGroup, 0f, 1f, 0.6f);
            float t = 0f;
            while (t < hold && !_skip) { t += Time.unscaledDeltaTime; yield return null; }
            _skip = false;
            yield return AlibiFx.Fade(chapterGroup, 1f, 0f, 0.6f);
            chapterGroup.blocksRaycasts = false;
            chapterGroup.gameObject.SetActive(false);
        }

        bool _skip;
        public void SkipChapter() => _skip = true;

        public IEnumerator Fade(float to, float seconds)
        {
            if (fadeGroup == null) yield break;
            fadeGroup.gameObject.SetActive(true);
            fadeGroup.blocksRaycasts = to > 0.5f;
            yield return AlibiFx.Fade(fadeGroup, fadeGroup.alpha, to, seconds);
            if (to <= 0.001f) fadeGroup.gameObject.SetActive(false);
        }

        public void SetFade(float a)
        {
            if (fadeGroup == null) return;
            fadeGroup.gameObject.SetActive(a > 0.001f);
            fadeGroup.alpha = a;
            fadeGroup.blocksRaycasts = a > 0.5f;
        }
    }
}
