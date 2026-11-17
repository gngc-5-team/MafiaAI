using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Mystery.UI
{
    /// <summary>
    /// 사건 재구성: 보고서 문장의 빈칸(붉은 밑줄)을 눌러, 지금까지 모은 단어 가운데서 고른다.
    /// 이름만 찍는 게 아니라 '누가·언제·무엇으로·어떤 속임수로·왜'를 전부 맞혀야 한다. 틀리면 30분.
    /// </summary>
    public class TheoryView : MonoBehaviour
    {
        public GameObject root;
        public RectTransform sentenceContent;
        public TMP_Text sentenceTemplate;       // TmpLinkClick 달림
        public TMP_Text feedback;
        public TMP_Text costNote;
        public Button submitButton;
        public Button closeButton;

        [Header("단어 고르기")]
        public GameObject pickerRoot;
        public TMP_Text pickerTitle;
        public RectTransform pickerContent;
        public Button wordTemplate;
        public Button pickerClose;

        public string blankColor = "#9E1B30";
        public string emptyColor = "#8A6D4E";

        public event Action Submitted;
        public event Action Closed;

        readonly Dictionary<string, string> _answers = new Dictionary<string, string>();
        readonly List<GameObject> _lines = new List<GameObject>();
        readonly List<GameObject> _words = new List<GameObject>();
        MysterySession _session;
        string _blank;

        public IReadOnlyDictionary<string, string> Answers => _answers;

        void Awake()
        {
            if (sentenceTemplate != null) sentenceTemplate.gameObject.SetActive(false);
            if (wordTemplate != null) wordTemplate.gameObject.SetActive(false);
            if (submitButton != null) submitButton.onClick.AddListener(() => Submitted?.Invoke());
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (pickerClose != null) pickerClose.onClick.AddListener(() => pickerRoot?.SetActive(false));
        }

        public bool Open => root != null && root.activeSelf;

        public void Show(MysterySession s)
        {
            _session = s;
            if (root != null) root.SetActive(true);
            if (pickerRoot != null) pickerRoot.SetActive(false);
            // 이제 쓸 수 없는 단어(얻지 못한 것)는 지운다
            foreach (var k in _answers.Keys.ToList())
                if (_answers[k] != null && !s.WordAvailable(s.Case.WordById(_answers[k]))) _answers.Remove(k);
            if (costNote != null) costNote.text = $"빈칸을 눌러 단어를 고른다 · 틀리면 {s.Case.WrongTheoryCost}분이 흐른다 · 지금까지 {s.TheoryAttempts}번 냈다";
            Render();
        }

        public void SetFeedback(string text) { if (feedback != null) feedback.text = text ?? ""; }

        void Render()
        {
            foreach (var g in _lines) if (g != null) Destroy(g);
            _lines.Clear();
            if (_session == null || sentenceTemplate == null) return;
            foreach (var sentence in _session.Case.Sentences)
            {
                var t = Instantiate(sentenceTemplate, sentenceContent);
                t.gameObject.SetActive(true);
                t.text = _session.RenderSentence(sentence, _answers, (id, shown) =>
                    $"<link=\"{id}\"><u><color={(_answers.ContainsKey(id) ? blankColor : emptyColor)}>{shown}</color></u></link>");
                var click = t.GetComponent<TmpLinkClick>();
                if (click != null) click.LinkClicked += OpenPicker;
                _lines.Add(t.gameObject);
            }
            if (submitButton != null) submitButton.interactable = _session.Case.Blanks.All(b => _answers.ContainsKey(b.Id));
        }

        public void OpenPicker(string blankId)
        {
            var blank = _session.Case.Blanks.FirstOrDefault(b => b.Id == blankId);
            if (blank == null || pickerRoot == null) return;
            _blank = blankId;
            pickerRoot.SetActive(true);
            if (pickerTitle != null) pickerTitle.text = KindLabel(blank.Kind);
            foreach (var g in _words) if (g != null) Destroy(g);
            _words.Clear();
            foreach (var w in _session.Words(blank.Kind))
            {
                var b = Instantiate(wordTemplate, pickerContent);
                b.gameObject.SetActive(true);
                var label = b.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = w.Label;
                string id = w.Id;
                b.onClick.AddListener(() => Pick(id));
                _words.Add(b.gameObject);
            }
        }

        void Pick(string wordId)
        {
            if (_blank == null) return;
            _answers[_blank] = wordId;
            if (pickerRoot != null) pickerRoot.SetActive(false);
            SetFeedback("");
            Render();
        }

        /// <summary>자동 검증용: 정답을 바로 채운다.</summary>
        public void Fill(IDictionary<string, string> answers)
        {
            _answers.Clear();
            foreach (var kv in answers) _answers[kv.Key] = kv.Value;
            Render();
        }

        static string KindLabel(WordKind k) => k switch
        {
            WordKind.Person => "누가",
            WordKind.Time => "언제",
            WordKind.Object => "무엇으로",
            WordKind.Deception => "어떤 속임수로",
            WordKind.Place => "어디서",
            _ => "무엇을 감추려고",
        };

        public void Close()
        {
            if (root != null) root.SetActive(false);
            if (pickerRoot != null) pickerRoot.SetActive(false);
            Closed?.Invoke();
        }
    }
}
