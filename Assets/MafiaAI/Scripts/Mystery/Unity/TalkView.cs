using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MafiaAI.Alibi.UI;

namespace MafiaAI.Mystery.UI
{
    /// <summary>
    /// 심문 화면: 손님 목록(액자 카드) → 마주 앉은 한 사람(초상 · 진술 목록 · 질문 쪽지 · 대사창 · 직접 묻기).
    /// 진술 한 줄을 눌러 고른 뒤 수첩에서 증거를 내밀면 '제시'가 된다. 최후의 추궁도 이 화면에서 한다.
    /// </summary>
    public class TalkView : MonoBehaviour
    {
        [Serializable]
        public class CardUI
        {
            public Button button;
            public Image art;
            public TMP_Text name;
            public TMP_Text note;
            public TMP_Text stamp;
            public Image deadMark;
        }

        [Serializable]
        public class StatementRow
        {
            public Button button;
            public Image frame;
            public TMP_Text text;
            public TMP_Text mark;
        }

        public GameObject root;
        public Image background;
        public Image redFlash;

        [Header("손님 목록")]
        public GameObject lineupRoot;
        public CardUI[] cards = new CardUI[5];
        public Image victimArt;
        public TMP_Text victimLabel;
        public Button lineupBack;

        [Header("마주 앉은 사람")]
        public GameObject talkRoot;
        public RectTransform portraitRect;
        public Image portrait;
        public TMP_Text nameText;
        public TMP_Text roleText;
        public TMP_Text bioText;
        public Button backButton;

        [Header("진술")]
        public RectTransform statementContent;
        public Button statementTemplate;     // 자식: Frame(Image) · Text · Mark
        public TMP_Text statementHint;

        [Header("질문 쪽지")]
        public RectTransform chipContent;
        public Button chipTemplate;

        [Header("대사창")]
        public TMP_Text speaker;
        public TMP_Text question;
        public TMP_Text line;
        public AlibiTypewriter typewriter;
        public Button boxButton;
        public TMP_InputField input;
        public Button askButton;

        [Header("최후의 추궁")]
        public GameObject confrontRoot;
        public TMP_Text confrontTitle;
        public Image[] resolveMarks;

        [Header("표기")]
        public Color cardAlive = Color.white;
        public Color cardDead = new Color(0.4f, 0.38f, 0.38f);
        public Color victimTint = new Color(0.5f, 0.5f, 0.52f);
        public Color flashColor = new Color(0.62f, 0.05f, 0.1f, 0.42f);
        public Color rowNormal = new Color(1f, 1f, 1f, 0f);
        public Color rowSelected = new Color(1f, 1f, 1f, 0.92f);   // 붉은 쪽지(TagCrimson)가 진술 밑에 깔린다
        public Color textNormal = new Color(0.94f, 0.9f, 0.82f);
        public Color textBroken = new Color(0.66f, 0.6f, 0.53f);
        public string unmetNote = "아직 대면 전";
        public string deadNote = "숨짐";
        public string brokenMark = "무너짐";
        public string selectedHint = "고른 진술에 맞설 증거를 수첩에서 골라 「제시」하라";
        public string idleHint = "진술 한 줄을 누르면 증거로 맞설 수 있다";
        public float breathAmount = 0.006f;

        public event Action<int> CardClicked;
        public event Action LineupBackClicked;
        public event Action BackClicked;
        public event Action<string> StatementClicked;
        public event Action<string> ChipClicked;
        public event Action<string> QuestionSubmitted;
        public event Action BoxClicked;

        readonly List<Button> _rows = new List<Button>();
        readonly List<Button> _chips = new List<Button>();
        bool _animating;
        bool _submitting;

        void Awake()
        {
            for (int i = 0; i < cards.Length; i++)
            {
                int k = i;
                if (cards[i]?.button != null) cards[i].button.onClick.AddListener(() => CardClicked?.Invoke(k));
            }
            if (lineupBack != null) lineupBack.onClick.AddListener(() => LineupBackClicked?.Invoke());
            if (backButton != null) backButton.onClick.AddListener(() => BackClicked?.Invoke());
            if (boxButton != null) boxButton.onClick.AddListener(() => BoxClicked?.Invoke());
            if (askButton != null) askButton.onClick.AddListener(SubmitDeferred);
            if (input != null) input.onSubmit.AddListener(_ => SubmitDeferred());
            if (statementTemplate != null) statementTemplate.gameObject.SetActive(false);
            if (chipTemplate != null) chipTemplate.gameObject.SetActive(false);
            if (redFlash != null) redFlash.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
            if (confrontRoot != null) confrontRoot.SetActive(false);
        }

        // 한글 IME: 한 프레임 미뤄 조합을 확정시킨 뒤 읽는다(v2에서 검증한 방식)
        void SubmitDeferred()
        {
            if (_submitting || input == null || !input.interactable) return;
            _submitting = true;
            StartCoroutine(SubmitAfterImeCommit());
        }

        IEnumerator SubmitAfterImeCommit()
        {
            input.DeactivateInputField();
            yield return null;
            yield return new WaitForEndOfFrame();
            _submitting = false;
            string q = input.text != null ? input.text.Trim() : "";
            if (q.Length == 0) { input.ActivateInputField(); yield break; }
            input.text = "";
            QuestionSubmitted?.Invoke(q);
        }

        void Update()
        {
            if (_animating || portraitRect == null || talkRoot == null || !talkRoot.activeSelf) return;
            float s = 1f + Mathf.Sin(Time.unscaledTime * 1.7f) * breathAmount;
            portraitRect.localScale = new Vector3(1f, s, 1f);
        }

        public bool Visible => root != null && root.activeSelf;
        public bool InTalk => talkRoot != null && talkRoot.activeSelf;
        public void SetVisible(bool on) { if (root != null) root.SetActive(on); }

        // ───────────── 손님 목록 ─────────────

        public void ShowLineup(MysterySession s, Func<string, Sprite> cardArt)
        {
            SetVisible(true);
            if (lineupRoot != null) lineupRoot.SetActive(true);
            if (talkRoot != null) talkRoot.SetActive(false);
            var c = s.Case;
            for (int i = 0; i < cards.Length; i++)
            {
                var ui = cards[i];
                if (ui == null) continue;
                bool on = i < c.Suspects.Length;
                if (ui.button != null) ui.button.gameObject.SetActive(on);
                if (!on) continue;
                var p = c.Suspects[i];
                bool dead = s.IsDead(p.Name);
                int broken = s.Statements(p.Name).Count(s.Broken);
                if (ui.art != null) { ui.art.sprite = cardArt?.Invoke(p.Name); ui.art.color = dead ? cardDead : cardAlive; }
                if (ui.name != null) ui.name.text = p.Name;
                if (ui.note != null) ui.note.text = dead ? deadNote : !s.HasMet(p.Name) ? unmetNote : p.Role;
                if (ui.stamp != null) ui.stamp.text = dead ? "" : broken > 0 ? $"깸 {broken}" : "";
                if (ui.deadMark != null) ui.deadMark.enabled = dead;
                if (ui.button != null) ui.button.interactable = !dead;
            }
            if (victimArt != null) { victimArt.sprite = cardArt?.Invoke(c.Victim); victimArt.color = victimTint; }
            if (victimLabel != null) victimLabel.text = "피해자 · " + c.Victim;
        }

        // ───────────── 마주 앉기 ─────────────

        public void ShowTalk(Suspect who, Sprite portraitSprite)
        {
            SetVisible(true);
            if (lineupRoot != null) lineupRoot.SetActive(false);
            if (talkRoot != null) talkRoot.SetActive(true);
            if (nameText != null) nameText.text = who.Name;
            if (roleText != null) roleText.text = who.Role;
            if (bioText != null) bioText.text = who.Bio;
            if (portrait != null) { portrait.sprite = portraitSprite; portrait.enabled = portraitSprite != null; portrait.color = Color.white; }
            if (portraitRect != null) portraitRect.localScale = Vector3.one;
        }

        public void RenderStatements(MysterySession s, string name, string selected)
        {
            foreach (var b in _rows) if (b != null) Destroy(b.gameObject);
            _rows.Clear();
            if (statementTemplate == null || statementContent == null) return;
            foreach (var st in s.Statements(name))
            {
                var b = Instantiate(statementTemplate, statementContent);
                b.gameObject.SetActive(true);
                bool broken = s.Broken(st);
                var text = b.transform.Find("Text")?.GetComponent<TMP_Text>();
                var mark = b.transform.Find("Mark")?.GetComponent<TMP_Text>();
                var frame = b.transform.Find("Frame")?.GetComponent<Image>();
                bool picked = st.Id == selected;
                if (text != null)
                {
                    text.text = broken ? $"<s>{st.Text}</s>" : "「" + st.Text + "」";
                    text.color = broken ? textBroken : picked ? Color.white : textNormal;
                }
                if (mark != null) mark.text = broken ? brokenMark : "";
                if (frame != null) frame.color = picked ? rowSelected : rowNormal;
                string id = st.Id;
                b.interactable = !broken && s.CanAct;
                b.onClick.AddListener(() => StatementClicked?.Invoke(id));
                _rows.Add(b);
            }
            if (statementHint != null) statementHint.text = selected != null ? selectedHint : idleHint;
        }

        public void RenderChips(IEnumerable<(string Id, string Label, bool Asked)> chips)
        {
            foreach (var b in _chips) if (b != null) Destroy(b.gameObject);
            _chips.Clear();
            if (chipTemplate == null || chipContent == null) return;
            foreach (var (id, label, asked) in chips)
            {
                var b = Instantiate(chipTemplate, chipContent);
                b.gameObject.SetActive(true);
                var t = b.GetComponentInChildren<TMP_Text>();
                if (t != null) { t.text = label; t.alpha = asked ? 0.6f : 1f; }
                string key = id;
                b.onClick.AddListener(() => ChipClicked?.Invoke(key));
                _chips.Add(b);
            }
        }

        public void SetInputEnabled(bool on)
        {
            if (input != null) input.interactable = on;
            if (askButton != null) askButton.interactable = on;
            foreach (var b in _chips) if (b != null) b.interactable = on;
        }

        // ───────────── 대사창 ─────────────

        public void Say(string who, string text, string asked = null)
        {
            if (speaker != null) speaker.text = who ?? "";
            if (question != null) question.text = asked ?? "";
            if (typewriter != null) typewriter.Play(text ?? "");
            else if (line != null) line.text = text ?? "";
        }

        public void Thinking(string who, string asked)
        {
            if (speaker != null) speaker.text = who ?? "";
            if (question != null) question.text = asked ?? "";
            typewriter?.Thinking();
        }

        public bool Typing => typewriter != null && typewriter.IsPlaying;
        public void CompleteTyping() { if (typewriter != null) typewriter.Complete(); }

        // ───────────── 최후의 추궁 ─────────────

        public void SetConfront(bool on, int resolveLeft = 0)
        {
            if (confrontRoot != null) confrontRoot.SetActive(on);
            if (backButton != null) backButton.gameObject.SetActive(!on);
            if (resolveMarks != null)
                for (int i = 0; i < resolveMarks.Length; i++)
                    if (resolveMarks[i] != null) resolveMarks[i].color = i < resolveLeft ? new Color(0.86f, 0.2f, 0.27f) : new Color(0.3f, 0.26f, 0.26f, 0.6f);
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

        /// <summary>진술이 무너졌을 때: 붉은 섬광 + 초상 흔들림.</summary>
        public IEnumerator PlayBreak()
        {
            _animating = true;
            if (redFlash != null) StartCoroutine(AlibiFx.Flash(redFlash, flashColor, 0.7f));
            if (portrait != null) StartCoroutine(AlibiFx.Flash(portrait, new Color(1f, 0.6f, 0.6f), 0.7f));
            if (portraitRect != null) yield return AlibiFx.Shake(portraitRect, 22f, 0.5f);
            _animating = false;
        }

        public IEnumerator PlayWrong()
        {
            if (redFlash != null) yield return AlibiFx.Flash(redFlash, new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * 0.45f), 0.45f);
        }

        public IEnumerator PlayNod()
        {
            if (portraitRect == null) yield break;
            _animating = true;
            yield return AlibiFx.ScaleTo(portraitRect, 1f, 1.02f, 0.15f);
            yield return AlibiFx.ScaleTo(portraitRect, 1.02f, 1f, 0.25f);
            _animating = false;
        }
    }
}
