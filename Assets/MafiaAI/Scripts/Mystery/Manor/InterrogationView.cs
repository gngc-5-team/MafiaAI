using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MafiaAI.Alibi.UI;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 심문: 저택 안에서 마주 선 두 사람을 카메라가 투샷으로 잡고, 오른쪽에 그 사람의 큰 초상이 어둠 속에서 떠오른다.
    /// 왼쪽엔 이름·진술(누르면 수첩에서 맞설 증거를 고른다)·질문 쪽지·직접 묻기. 대사는 아래 레터박스 자막으로 흐른다.
    /// </summary>
    public class InterrogationView : MonoBehaviour
    {
        public CanvasGroup group;
        public RectTransform portraitRect;
        public Image portrait;
        public CanvasGroup portraitGroup;
        public Image redFlash;

        [Header("왼쪽")]
        public TMP_Text nameText;
        public TMP_Text roleText;
        public TMP_Text bioText;
        public RectTransform statementContent;
        public Button statementTemplate;     // 자식: Frame · Text · Mark
        public TMP_Text statementHint;
        public RectTransform chipContent;
        public Button chipTemplate;
        public TMP_InputField input;
        public Button askButton;
        public Button backButton;
        public Button evidenceButton;

        [Header("최후의 추궁")]
        public GameObject confrontRoot;
        public Image[] resolveMarks;

        [Header("표기")]
        public Color rowNormal = new Color(1f, 1f, 1f, 0f);
        public Color rowSelected = new Color(1f, 1f, 1f, 0.92f);
        public Color textNormal = new Color(0.93f, 0.89f, 0.81f);
        public Color textBroken = new Color(0.6f, 0.55f, 0.5f);
        public Color flashColor = new Color(0.62f, 0.05f, 0.1f, 0.42f);
        public string brokenMark = "무너짐";
        public string selectedHint = "고른 진술에 맞설 증거를 수첩에서 골라 「제시」";
        public string idleHint = "진술을 누르면 증거로 맞선다 · TAB 수첩";
        public float breathAmount = 0.005f;

        public event Action BackClicked;
        public event Action EvidenceClicked;
        public event Action<string> StatementClicked;
        public event Action<string> ChipClicked;
        public event Action<string> QuestionSubmitted;

        readonly List<Button> _rows = new List<Button>();
        readonly List<Button> _chips = new List<Button>();
        bool _submitting;
        bool _animating;

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(() => BackClicked?.Invoke());
            if (evidenceButton != null) evidenceButton.onClick.AddListener(() => EvidenceClicked?.Invoke());
            if (askButton != null) askButton.onClick.AddListener(SubmitDeferred);
            if (input != null) input.onSubmit.AddListener(_ => SubmitDeferred());
            if (statementTemplate != null) statementTemplate.gameObject.SetActive(false);
            if (chipTemplate != null) chipTemplate.gameObject.SetActive(false);
            if (redFlash != null) redFlash.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
        }

        public bool Open => group != null && group.gameObject.activeSelf && group.alpha > 0.01f;

        // 한글 IME: 한 프레임 미뤄 조합을 확정시킨 뒤 읽는다
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
            if (_animating || portraitRect == null || !Open) return;
            float s = 1f + Mathf.Sin(Time.unscaledTime * 1.6f) * breathAmount;
            portraitRect.localScale = new Vector3(1f, s, 1f);
        }

        public IEnumerator Show(Suspect who, Sprite portraitSprite)
        {
            if (group == null) yield break;
            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;
            if (nameText != null) nameText.text = who.Name;
            if (roleText != null) roleText.text = who.Role;
            if (bioText != null) bioText.text = who.Bio;
            if (portrait != null) { portrait.sprite = portraitSprite; portrait.enabled = portraitSprite != null; portrait.color = Color.white; }
            if (portraitRect != null) portraitRect.localScale = Vector3.one;
            if (portraitGroup != null) portraitGroup.alpha = 0f;
            yield return AlibiFx.Fade(group, group.alpha, 1f, 0.35f);
            if (portraitRect != null && portraitGroup != null)
            {
                _animating = true;
                StartCoroutine(AlibiFx.Fade(portraitGroup, 0f, 1f, 0.6f));
                yield return AlibiFx.SlideY(portraitRect, -36f, 0.6f);
                _animating = false;
            }
        }

        public IEnumerator Hide()
        {
            if (group == null || !group.gameObject.activeSelf) yield break;
            group.blocksRaycasts = false;
            yield return AlibiFx.Fade(group, group.alpha, 0f, 0.3f);
            group.gameObject.SetActive(false);
        }

        public void HideNow()
        {
            if (group == null) return;
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.gameObject.SetActive(false);
        }

        public void SetPortraitVisible(bool on)
        {
            if (portraitGroup != null) StartCoroutine(AlibiFx.Fade(portraitGroup, portraitGroup.alpha, on ? 1f : 0f, 0.25f));
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
                bool picked = st.Id == selected;
                var text = b.transform.Find("Text")?.GetComponent<TMP_Text>();
                var mark = b.transform.Find("Mark")?.GetComponent<TMP_Text>();
                var frame = b.transform.Find("Frame")?.GetComponent<Image>();
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
                if (t != null) { t.text = label; t.alpha = asked ? 0.55f : 1f; }
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

        public bool InputFocused => input != null && input.isFocused;

        public void SetConfront(bool on, int resolveLeft = 0)
        {
            if (confrontRoot != null) confrontRoot.SetActive(on);
            if (backButton != null) backButton.gameObject.SetActive(!on);
            if (chipContent != null) chipContent.gameObject.SetActive(!on);
            if (input != null) input.gameObject.SetActive(!on);
            if (askButton != null) askButton.gameObject.SetActive(!on);
            if (resolveMarks != null)
                for (int i = 0; i < resolveMarks.Length; i++)
                    if (resolveMarks[i] != null) resolveMarks[i].color = i < resolveLeft ? new Color(0.86f, 0.2f, 0.27f) : new Color(0.3f, 0.26f, 0.26f, 0.6f);
        }

        public IEnumerator PlayBreak(RectTransform shake)
        {
            _animating = true;
            if (redFlash != null) StartCoroutine(AlibiFx.Flash(redFlash, flashColor, 0.7f));
            if (portrait != null) StartCoroutine(AlibiFx.Flash(portrait, new Color(1f, 0.6f, 0.6f), 0.7f));
            if (portraitRect != null) yield return AlibiFx.Shake(portraitRect, 20f, 0.5f);
            _animating = false;
        }

        public IEnumerator PlayWrong()
        {
            if (redFlash != null) yield return AlibiFx.Flash(redFlash, new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * 0.45f), 0.45f);
        }
    }
}
