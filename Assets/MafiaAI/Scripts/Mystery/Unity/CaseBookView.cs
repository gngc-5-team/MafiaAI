using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Mystery.UI
{
    /// <summary>
    /// 오른쪽 수사 수첩: 증거품(격자 + 자세히) · 타임라인(시각순 사실) · 원고(장별 읽기) · 의문점(풀린 것/남은 것).
    /// 맨 아래 「사건 재구성」. 증거 상세의 행동 버튼은 상황에 따라 보여 주기 / 이 진술에 제시 / 반박한다로 바뀐다.
    /// 목록 줄은 저작해 둔 틀을 복제한다.
    /// </summary>
    public class CaseBookView : MonoBehaviour
    {
        public enum ActMode { None, Show, Present, Rebut }

        [Header("탭")]
        public Button[] tabButtons = new Button[4];
        public Image[] tabMarks = new Image[4];
        public GameObject[] tabPanels = new GameObject[4];
        public TMP_Text[] tabLabels = new TMP_Text[4];

        [Header("증거품")]
        public RectTransform evidenceContent;
        public Button evidenceTemplate;         // 자식: Frame · Art · Name · New
        public TMP_Text evidenceEmpty;
        public GameObject detailRoot;
        public Image detailArt;
        public TMP_Text detailName;
        public TMP_Text detailSource;
        public TMP_Text detailText;
        public TMP_Text detailFound;
        public Button inspectButton;
        public Button readButton;
        public Button actButton;
        public TMP_Text actLabel;

        [Header("타임라인")]
        public RectTransform timelineContent;
        public TMP_Text timelineTemplate;

        [Header("원고")]
        public RectTransform chapterContent;
        public Button chapterTemplate;          // 자식: Title · State

        [Header("의문점")]
        public RectTransform questionContent;
        public TMP_Text questionTemplate;

        [Header("하단")]
        public Button theoryButton;
        public TMP_Text theoryLabel;
        public TMP_Text counts;

        [Header("표기")]
        public Color frameNormal = new Color(1, 1, 1, 0);
        public Color frameSelected = new Color(0.62f, 0.11f, 0.19f, 0.35f);
        public string showLabel = "보여 주기";
        public string presentLabel = "이 진술에 제시";
        public string rebutLabel = "이것으로 반박";
        public string theoryNormal = "사건 재구성";
        public string theoryConfront = "최후의 추궁";

        public event Action<string> EvidenceSelected;
        public event Action<string> InspectClicked;
        public event Action<string> ReadClicked;
        public event Action<string> ActClicked;
        public event Action<int> ChapterClicked;
        public event Action TheoryClicked;
        public event Action<int> TabChanged;

        public int Tab { get; private set; }
        public string Selected { get; private set; }
        public ActMode Mode { get; private set; } = ActMode.None;

        readonly HashSet<string> _seen = new HashSet<string>();
        readonly List<GameObject> _evRows = new List<GameObject>();
        readonly List<GameObject> _tlRows = new List<GameObject>();
        readonly List<GameObject> _chRows = new List<GameObject>();
        readonly List<GameObject> _qRows = new List<GameObject>();
        MysterySession _session;
        Func<string, Sprite> _art;

        void Awake()
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int k = i;
                if (tabButtons[i] != null) tabButtons[i].onClick.AddListener(() => SetTab(k));
            }
            if (inspectButton != null) inspectButton.onClick.AddListener(() => { if (Selected != null) InspectClicked?.Invoke(Selected); });
            if (readButton != null) readButton.onClick.AddListener(() => { if (Selected != null) ReadClicked?.Invoke(Selected); });
            if (actButton != null) actButton.onClick.AddListener(() => { if (Selected != null) ActClicked?.Invoke(Selected); });
            if (theoryButton != null) theoryButton.onClick.AddListener(() => TheoryClicked?.Invoke());
            foreach (var t in new Component[] { evidenceTemplate, timelineTemplate, chapterTemplate, questionTemplate })
                if (t != null) t.gameObject.SetActive(false);
            SetTab(0);
        }

        public void SetTab(int k)
        {
            Tab = k;
            for (int i = 0; i < tabPanels.Length; i++)
            {
                if (tabPanels[i] != null) tabPanels[i].SetActive(i == k);
                if (tabMarks[i] != null) tabMarks[i].enabled = i == k;
                if (tabLabels[i] != null) tabLabels[i].alpha = i == k ? 1f : 0.55f;
            }
            TabChanged?.Invoke(k);
        }

        public void SetMode(ActMode mode)
        {
            Mode = mode;
            RenderDetail();
        }

        public void Select(string evidenceId)
        {
            Selected = evidenceId;
            if (evidenceId != null) _seen.Add(evidenceId);
            Render(_session, _art);
            EvidenceSelected?.Invoke(evidenceId);
        }

        // ───────────── 그리기 ─────────────

        public void Render(MysterySession s, Func<string, Sprite> art)
        {
            if (s == null) return;
            _session = s;
            _art = art;
            RenderEvidence();
            RenderDetail();
            RenderTimeline();
            RenderChapters();
            RenderQuestions();
            int open = s.OpenQuestions.Count();
            if (counts != null) counts.text = $"증거 {s.Found.Count} · 남은 의문 {open}";
            if (tabLabels.Length > 3 && tabLabels[3] != null) tabLabels[3].text = open > 0 ? $"의문점 {open}" : "의문점";
            if (theoryLabel != null) theoryLabel.text = s.Has("theory_ok") ? theoryConfront : theoryNormal;
            if (theoryButton != null) theoryButton.interactable = s.Phase == Phase.Investigation;
        }

        void Clear(List<GameObject> rows)
        {
            foreach (var g in rows) if (g != null) Destroy(g);
            rows.Clear();
        }

        void RenderEvidence()
        {
            Clear(_evRows);
            if (evidenceTemplate == null || evidenceContent == null) return;
            // 원고 장은 '원고' 탭에서 읽는다 — 증거 격자에는 원고 뭉치·타자기·조각 같은 물건과 증언만
            var list = _session.FoundEvidence.Where(e => e.Chapter == 0 || e.Id == "typewriter" || e.Id == "fragment").ToList();
            list.AddRange(_session.FoundEvidence.Where(e => e.Chapter > 0 && e.Id != "typewriter" && e.Id != "fragment"));
            if (evidenceEmpty != null) evidenceEmpty.gameObject.SetActive(list.Count == 0);
            foreach (var e in list)
            {
                var b = Instantiate(evidenceTemplate, evidenceContent);
                b.gameObject.SetActive(true);
                var artImg = b.transform.Find("Art")?.GetComponent<Image>();
                var nameTxt = b.transform.Find("Name")?.GetComponent<TMP_Text>();
                var dot = b.transform.Find("New");
                var frame = b.transform.Find("Frame")?.GetComponent<Image>();
                if (artImg != null) { var sp = _art?.Invoke(ArtKey(e)); artImg.sprite = sp; artImg.enabled = sp != null; artImg.preserveAspect = true; }
                if (nameTxt != null) nameTxt.text = ShortName(e);
                if (dot != null) dot.gameObject.SetActive(!_seen.Contains(e.Id));
                if (frame != null) frame.color = e.Id == Selected ? frameSelected : frameNormal;
                string id = e.Id;
                b.onClick.AddListener(() => Select(id));
                _evRows.Add(b.gameObject);
            }
        }

        public static string ArtKey(Evidence e) =>
            !string.IsNullOrEmpty(e.Image) ? e.Image : e.Kind == EvidenceKind.Testimony ? "face:" + e.Source.Split(' ')[0] : "ui_testimony";

        static string ShortName(Evidence e)
        {
            var n = e.Name;
            int dash = n.IndexOf(" — ");
            if (e.Kind == EvidenceKind.Testimony && dash > 0) return n.Substring(dash + 3);
            if (e.Id == "manuscript") return "원고 뭉치";
            int open = n.IndexOf('「'), close = n.IndexOf('」');
            if (e.Chapter > 0 && n.StartsWith("원고") && open > 0 && close > open) return $"{e.Chapter}장 {n.Substring(open + 1, close - open - 1)}";
            return n;
        }

        void RenderDetail()
        {
            if (detailRoot == null || _session == null) return;
            var e = Selected != null ? _session.Case.Ev(Selected) : null;
            detailRoot.SetActive(e != null);
            if (e == null) return;
            if (detailArt != null) { var sp = _art?.Invoke(ArtKey(e)); detailArt.sprite = sp; detailArt.enabled = sp != null; detailArt.preserveAspect = true; }
            if (detailName != null) detailName.text = e.Name;
            if (detailSource != null) detailSource.text = e.Kind == EvidenceKind.Testimony ? "증언 · " + e.Source : e.Source;
            if (detailText != null) detailText.text = e.Text;
            var found = e.Details.Where(d => _session.DetailFound(d.Id)).Select(d => "◆ " + d.Label).ToList();
            if (detailFound != null)
                detailFound.text = found.Count > 0 ? string.Join("   ", found)
                    : e.Details.Length > 0 ? "<color=#8A6D4E>확대해서 살펴볼 수 있다.</color>" : "";
            if (inspectButton != null) inspectButton.gameObject.SetActive(e.Image != null && e.Kind != EvidenceKind.Testimony);
            if (readButton != null) readButton.gameObject.SetActive(!string.IsNullOrEmpty(e.Document));
            if (actButton != null)
            {
                actButton.gameObject.SetActive(Mode != ActMode.None);
                if (actLabel != null) actLabel.text = Mode == ActMode.Present ? presentLabel : Mode == ActMode.Rebut ? rebutLabel : showLabel;
            }
        }

        void RenderTimeline()
        {
            Clear(_tlRows);
            if (timelineTemplate == null || timelineContent == null) return;
            foreach (var t in _session.Timeline)
            {
                var row = Instantiate(timelineTemplate, timelineContent);
                row.gameObject.SetActive(true);
                row.text = $"<b>{MysteryCase.Clock(t.At)}</b>   {t.Text}";
                _tlRows.Add(row.gameObject);
            }
        }

        void RenderChapters()
        {
            Clear(_chRows);
            if (chapterTemplate == null || chapterContent == null) return;
            string[] titles = { "만찬의 초대", "빚진 집사", "가짜 거장", "그림자 작가", "다락방의 아이", "흰 가운의 침묵", "자정의 불" };
            for (int i = 1; i <= 7; i++)
            {
                var have = _session.FoundEvidence.Where(e => e.Chapter == i).ToList();
                var b = Instantiate(chapterTemplate, chapterContent);
                b.gameObject.SetActive(true);
                var title = b.transform.Find("Title")?.GetComponent<TMP_Text>();
                var state = b.transform.Find("State")?.GetComponent<TMP_Text>();
                string status = have.Count == 0 ? (i == 6 && _session.Has("ch6_burned") ? "재가 되었다" : "없음")
                    : have.Any(e => e.Id == "fragment") && !have.Any(e => e.Id == "ch6") ? "타다 남은 조각" : i == 7 ? "두 줄뿐" : "읽기";
                if (title != null) { title.text = $"제{i}장  「{titles[i - 1]}」"; title.alpha = have.Count > 0 ? 1f : 0.45f; }
                if (state != null) state.text = status;
                int chapter = i;
                b.interactable = have.Count > 0;
                b.onClick.AddListener(() => ChapterClicked?.Invoke(chapter));
                _chRows.Add(b.gameObject);
            }
        }

        void RenderQuestions()
        {
            Clear(_qRows);
            if (questionTemplate == null || questionContent == null) return;
            foreach (var q in _session.OpenQuestions)
            {
                var row = Instantiate(questionTemplate, questionContent);
                row.gameObject.SetActive(true);
                row.text = $"<b>？ {q.Text}</b>\n<size=82%><color=#6B5440>{q.Hint}</color></size>";
                _qRows.Add(row.gameObject);
            }
            foreach (var q in _session.SolvedQuestions)
            {
                var row = Instantiate(questionTemplate, questionContent);
                row.gameObject.SetActive(true);
                row.text = $"<color=#7A6A58><s>{q.Text}</s></color>\n<size=82%><color=#2F5A8C>→ {q.Answer}</color></size>";
                _qRows.Add(row.gameObject);
            }
        }
    }
}
