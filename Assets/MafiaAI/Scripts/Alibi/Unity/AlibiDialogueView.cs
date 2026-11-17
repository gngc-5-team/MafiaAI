using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Alibi.UI
{
    /// <summary>
    /// 아래쪽 대사창(잼 텍스트박스 장식 그대로) + 질문 쪽지 + 추궁 꼬리표 + 대화 기록.
    /// 늘 '지금 오간 한 마디'만 크게 보여 준다(소설처럼). 지난 대화는 [기록]에서.
    /// 질문 쪽지·추궁 꼬리표·기록 줄은 개수가 바뀌어서 씬에 저작된 템플릿을 복제한다(나머지는 전부 씬 저작).
    /// </summary>
    public class AlibiDialogueView : MonoBehaviour
    {
        [Header("대사창")]
        public TMP_Text speaker;
        public TMP_Text question;
        public TMP_Text line;
        public AlibiTypewriter typewriter;
        public Button boxButton;            // 대사창을 누르면 타자기를 건너뛴다
        public Button historyButton;

        [Header("질문")]
        public RectTransform chipContent;
        public Button chipTemplate;
        public TMP_InputField input;
        public Button askButton;

        [Header("추궁")]
        public GameObject pressRoot;
        public RectTransform pressContent;
        public Button pressTemplate;

        [Header("기록")]
        public GameObject historyRoot;
        public ScrollRect historyScroll;
        public RectTransform historyContent;
        public TMP_Text historyLineTemplate;
        public Button historyClose;

        [Header("표기(내용 형식)")]
        public string questionFormat = "탐정 — {0}";
        public string narratorName = "수사 기록";
        public string detectiveFormat = "<color=#7A5A3A><b>탐정</b></color>  {0}";
        public string suspectFormat = "<color=#8E1B2E><b>{0}</b></color>  {1}";
        public string pressFormat = "{0}";

        public event Action<string, bool> QuestionSubmitted;   // (질문, 질문 쪽지에서 왔나)
        public event Action<int> PressClicked;

        readonly List<Button> _chips = new List<Button>();
        readonly List<string> _chipQuestions = new List<string>();
        readonly List<Button> _presses = new List<Button>();
        readonly List<int> _pressIds = new List<int>();
        readonly List<TMP_Text> _history = new List<TMP_Text>();
        bool _submitting;

        void Awake()
        {
            if (chipTemplate != null) chipTemplate.gameObject.SetActive(false);
            if (pressTemplate != null) pressTemplate.gameObject.SetActive(false);
            if (historyLineTemplate != null) historyLineTemplate.gameObject.SetActive(false);
            if (historyRoot != null) historyRoot.SetActive(false);
            if (input != null) input.onSubmit.AddListener(_ => SubmitDeferred());
            if (askButton != null) askButton.onClick.AddListener(SubmitDeferred);
            if (boxButton != null) boxButton.onClick.AddListener(() => typewriter?.Complete());
            if (historyButton != null) historyButton.onClick.AddListener(() => { if (historyRoot != null) historyRoot.SetActive(!historyRoot.activeSelf); });
            if (historyClose != null) historyClose.onClick.AddListener(HideHistory);
        }

        // ───────────── 입력(한글 IME: 한 프레임 미뤄 조합을 확정시킨 뒤 읽는다) ─────────────

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
            QuestionSubmitted?.Invoke(q, false);
        }

        public void FocusInput()
        {
            if (input != null && input.interactable && input.gameObject.activeInHierarchy) input.ActivateInputField();
        }

        public void SetInput(bool enabled, bool visible)
        {
            if (input != null)
            {
                input.gameObject.SetActive(visible);
                input.interactable = enabled;
            }
            if (askButton != null)
            {
                askButton.gameObject.SetActive(visible);
                askButton.interactable = enabled;
            }
        }

        // ───────────── 대사 ─────────────

        public void ShowNarration(string text)
        {
            if (speaker != null) speaker.text = narratorName;
            if (question != null) question.text = "";
            typewriter?.Show(text);
        }

        public void ShowExchange(string who, string asked, string answer, bool animate)
        {
            if (speaker != null) speaker.text = who;
            if (question != null) question.text = string.IsNullOrEmpty(asked) ? "" : string.Format(questionFormat, asked);
            if (typewriter == null) return;
            if (animate) typewriter.Play(answer); else typewriter.Show(answer);
        }

        public void ShowThinking(string who, string asked)
        {
            if (speaker != null) speaker.text = who;
            if (question != null) question.text = string.IsNullOrEmpty(asked) ? "" : string.Format(questionFormat, asked);
            typewriter?.Thinking();
        }

        // ───────────── 질문 쪽지 · 추궁 꼬리표 ─────────────

        public void SetChips(IReadOnlyList<(string Label, string Question)> chips, bool enabled)
        {
            while (_chips.Count < chips.Count && chipTemplate != null && chipContent != null)
            {
                var b = Instantiate(chipTemplate, chipContent);
                int slot = _chips.Count;
                b.onClick.AddListener(() => { if (slot < _chipQuestions.Count) QuestionSubmitted?.Invoke(_chipQuestions[slot], true); });
                _chips.Add(b);
            }
            _chipQuestions.Clear();
            for (int i = 0; i < _chips.Count; i++)
            {
                bool on = i < chips.Count;
                _chips[i].gameObject.SetActive(on);
                if (!on) continue;
                _chipQuestions.Add(chips[i].Question);
                _chips[i].interactable = enabled;
                var label = _chips[i].GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = chips[i].Label;
            }
        }

        public void SetPresses(IReadOnlyList<(int Id, string Label, bool Enabled)> presses)
        {
            if (pressRoot != null) pressRoot.SetActive(presses.Count > 0);
            while (_presses.Count < presses.Count && pressTemplate != null && pressContent != null)
            {
                var b = Instantiate(pressTemplate, pressContent);
                int slot = _presses.Count;
                b.onClick.AddListener(() => { if (slot < _pressIds.Count) PressClicked?.Invoke(_pressIds[slot]); });
                _presses.Add(b);
            }
            _pressIds.Clear();
            for (int i = 0; i < _presses.Count; i++)
            {
                bool on = i < presses.Count;
                _presses[i].gameObject.SetActive(on);
                if (!on) continue;
                _pressIds.Add(presses[i].Id);
                _presses[i].interactable = presses[i].Enabled;
                var label = _presses[i].GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = string.Format(pressFormat, presses[i].Label);
            }
        }

        // ───────────── 기록 ─────────────

        public bool HistoryOpen => historyRoot != null && historyRoot.activeSelf;
        public void HideHistory() { if (historyRoot != null) historyRoot.SetActive(false); }

        public void SetHistory(IReadOnlyList<(string Who, string Text)> chat)
        {
            while (_history.Count < chat.Count && historyLineTemplate != null && historyContent != null)
            {
                var t = Instantiate(historyLineTemplate, historyContent);
                _history.Add(t);
            }
            for (int i = 0; i < _history.Count; i++)
            {
                bool on = i < chat.Count;
                _history[i].gameObject.SetActive(on);
                if (!on) continue;
                var (who, text) = chat[i];
                _history[i].text = who == AlibiSession.Detective ? string.Format(detectiveFormat, text) : string.Format(suspectFormat, who, text);
            }
            if (historyScroll != null && HistoryOpen)
            {
                Canvas.ForceUpdateCanvases();
                historyScroll.verticalNormalizedPosition = 0f;
            }
        }
    }
}
