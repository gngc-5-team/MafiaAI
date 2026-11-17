using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Alibi.UI
{
    /// <summary>
    /// 왼쪽 무대. 평소엔 액자 카드로 용의자 다섯을 세워 두고, 고르면 그 사람이 크게 나와 마주 앉는다.
    /// 화면의 주인공은 표가 아니라 사람이다 — '작업처럼 느껴지는' 대시보드 구도를 뒤집은 핵심.
    /// 씬 저작 오브젝트에 내용(그림·글자·색)만 넣고, 반응 연출은 기준 위치에서 잠깐 흔들었다가 되돌린다.
    /// </summary>
    public class AlibiStageView : MonoBehaviour
    {
        [Serializable]
        public class CardUI
        {
            public Button button;
            public RectTransform rect;
            public Image art;
            public TMP_Text name;
            public TMP_Text note;
            public TMP_Text stamp;
            public Image deadMark;
        }

        [Header("무대")]
        public Image background;
        public Sprite[] roomBackgrounds;       // 0 = 용의자 목록, 1.. = 심문 장면(사람마다 번갈아)
        public Image redFlash;                 // 평소 투명, 해명 실패·오답 때 번쩍
        public Image dim;                      // 고백 장면에서 어두워진다

        [Header("용의자 목록")]
        public GameObject lineupRoot;
        public CardUI[] cards = new CardUI[5];
        public Image victimArt;
        public TMP_Text victimLabel;
        public TMP_Text lineupHint;

        [Header("심문 장면")]
        public GameObject talkRoot;
        public RectTransform portraitRect;
        public Image portrait;
        public TMP_Text dossierName;
        public TMP_Text dossierTagline;
        public Button backButton;

        [Header("색")]
        public Color cardAlive = Color.white;
        public Color cardDead = new Color(0.4f, 0.38f, 0.38f);
        public Color victimTint = new Color(0.5f, 0.5f, 0.52f);
        public Color flashColor = new Color(0.62f, 0.05f, 0.1f, 0.42f);

        [Header("표기(내용 형식)")]
        public string unmetNote = "아직 진술 전";
        public string metNote = "";
        public string deadNote = "숨진 채 발견";
        public string evadedStamp = "거짓";
        public string confessedStamp = "고백";
        public string victimFormat = "피해자 · {0}";
        public float breathAmount = 0.006f;

        public event Action<int> CardClicked;
        public event Action BackClicked;

        bool _animating;
        float _baseScale = 1f;   // 고백 뒤엔 다가선 채로 머문다(숨쉬기는 이 기준 위에서)

        void Awake()
        {
            for (int i = 0; i < cards.Length; i++)
            {
                int k = i;
                if (cards[i]?.button != null) cards[i].button.onClick.AddListener(() => CardClicked?.Invoke(k));
            }
            if (backButton != null) backButton.onClick.AddListener(() => BackClicked?.Invoke());
            if (redFlash != null) redFlash.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
            if (dim != null) dim.color = new Color(0, 0, 0, 0f);
        }

        void Update()
        {
            // 숨 쉬는 듯한 미세한 움직임 — 정지 화면이 아니라 '마주 앉은 사람'처럼 보이게
            if (_animating || portraitRect == null || talkRoot == null || !talkRoot.activeSelf) return;
            float s = 1f + Mathf.Sin(Time.unscaledTime * 1.7f) * breathAmount;
            portraitRect.localScale = new Vector3(_baseScale, _baseScale * s, 1f);
        }

        public bool InTalk => talkRoot != null && talkRoot.activeSelf;

        public void ShowLineup()
        {
            if (lineupRoot != null) lineupRoot.SetActive(true);
            if (talkRoot != null) talkRoot.SetActive(false);
            SetBackground(0);
        }

        public void ShowTalk(string name, string tagline, Sprite portraitSprite, int backgroundIndex)
        {
            if (lineupRoot != null) lineupRoot.SetActive(false);
            if (talkRoot != null) talkRoot.SetActive(true);
            if (dossierName != null) dossierName.text = name;
            if (dossierTagline != null) dossierTagline.text = tagline;
            if (portrait != null) { portrait.sprite = portraitSprite; portrait.enabled = portraitSprite != null; portrait.color = Color.white; }
            _baseScale = 1f;
            if (portraitRect != null) portraitRect.localScale = Vector3.one;
            if (dim != null) dim.color = new Color(0, 0, 0, 0f);
            SetBackground(backgroundIndex);
        }

        void SetBackground(int index)
        {
            if (background == null || roomBackgrounds == null || roomBackgrounds.Length == 0) return;
            var sp = roomBackgrounds[Mathf.Clamp(index, 0, roomBackgrounds.Length - 1)];
            if (sp != null) background.sprite = sp;
        }

        public void RenderLineup(AlibiSession s, Func<string, Sprite> cardArt)
        {
            var c = s.Case;
            for (int i = 0; i < cards.Length && i < c.Suspects.Count; i++)
            {
                var ui = cards[i];
                if (ui == null) continue;
                var p = c.Suspects[i];
                bool evaded = false;
                foreach (var r in s.Contradictions)
                    if (r.Reactions.TryGetValue(p.Name, out var re) && re == PressReaction.Evade) evaded = true;
                if (ui.art != null) { ui.art.sprite = cardArt?.Invoke(p.Name); ui.art.color = p.Dead ? cardDead : cardAlive; }
                if (ui.name != null) ui.name.text = p.Name;
                if (ui.note != null) ui.note.text = p.Dead ? deadNote : s.HasMet(p.Name) ? metNote : unmetNote;
                if (ui.stamp != null) ui.stamp.text = p.Dead ? "" : evaded ? evadedStamp : p.Confessed ? confessedStamp : "";
                if (ui.deadMark != null) ui.deadMark.enabled = p.Dead;
                if (ui.button != null) ui.button.interactable = !p.Dead && !s.Over;
            }
            if (victimArt != null) { victimArt.sprite = cardArt?.Invoke(c.Victim); victimArt.color = victimTint; }
            if (victimLabel != null) victimLabel.text = string.Format(victimFormat, c.Victim);
        }

        // ───────────── 반응 연출 ─────────────

        public IEnumerator PlayEnter()
        {
            if (portraitRect == null) yield break;
            _animating = true;
            var cg = portrait != null ? portrait.GetComponent<CanvasGroup>() : null;
            if (cg != null) cg.alpha = 0f;
            var fade = cg != null ? StartCoroutine(AlibiFx.Fade(cg, 0f, 1f, 0.35f)) : null;
            yield return AlibiFx.SlideY(portraitRect, -40f, 0.45f);
            if (fade != null) yield return fade;
            _animating = false;
        }

        /// <summary>해명 실패: 초상이 흔들리고 무대가 붉게 번쩍인다.</summary>
        public IEnumerator PlayEvade()
        {
            _animating = true;
            if (redFlash != null) StartCoroutine(AlibiFx.Flash(redFlash, flashColor, 0.7f));
            if (portrait != null) StartCoroutine(AlibiFx.Flash(portrait, new Color(1f, 0.55f, 0.55f), 0.7f));
            yield return AlibiFx.Shake(portraitRect, 22f, 0.5f);
            _animating = false;
        }

        /// <summary>고백: 주변이 어두워지고 그 사람에게 천천히 다가간다.</summary>
        public IEnumerator PlayConfess()
        {
            _animating = true;
            if (dim != null) StartCoroutine(AlibiFx.Tween(0.8f, k => dim.color = new Color(0, 0, 0, Mathf.Lerp(0f, 0.45f, k))));
            yield return AlibiFx.ScaleTo(portraitRect, _baseScale, 1.07f, 1.2f);
            _baseScale = 1.07f;
            _animating = false;
        }

        /// <summary>진술 유지: 작게 고개를 끄덕이듯.</summary>
        public IEnumerator PlayMaintain()
        {
            _animating = true;
            yield return AlibiFx.ScaleTo(portraitRect, _baseScale, _baseScale * 1.025f, 0.15f);
            yield return AlibiFx.ScaleTo(portraitRect, _baseScale * 1.025f, _baseScale, 0.25f);
            _animating = false;
        }

        public IEnumerator PlayWrong()
        {
            if (redFlash != null) yield return AlibiFx.Flash(redFlash, new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * 0.5f), 0.45f);
        }
    }
}
