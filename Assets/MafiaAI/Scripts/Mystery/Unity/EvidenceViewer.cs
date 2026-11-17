using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Mystery.UI
{
    /// <summary>
    /// 증거를 크게 펼쳐 놓고 들여다보는 화면. 그림 위를 훑다가 숨은 단서 자리에 오면 붉은 동그라미가 뜨고, 누르면 드러난다.
    /// (단서 자리는 처음엔 보이지 않는다 — 스스로 찾는 맛) 시간은 들지 않는다.
    /// </summary>
    public class EvidenceViewer : MonoBehaviour
    {
        public GameObject root;
        public Image art;
        public RectTransform detailLayer;
        public Button detailTemplate;          // 자식 "Found"(찾은 뒤 남는 표시)
        public RectTransform hoverRing;
        public TMP_Text nameText;
        public TMP_Text sourceText;
        public TMP_Text bodyText;
        public TMP_Text foundText;
        public TMP_Text hintText;
        public Button closeButton;
        public string hintSearching = "그림을 천천히 훑어보자. 이상한 곳에 마우스를 대면 붉은 동그라미가 뜬다.";
        public string hintAllFound = "더 찾을 것은 없어 보인다.";
        public string hintNone = "";

        public event Action<string, string> DetailClicked;   // (증거, 단서)
        public event Action Closed;

        readonly List<Button> _spots = new List<Button>();
        string _evidence;

        void Awake()
        {
            if (detailTemplate != null) detailTemplate.gameObject.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            // root는 씬에서 꺼진 채 저작된다 — 여기서 끄면 처음 켜질 때(Awake) 곧바로 다시 꺼진다
            if (hoverRing != null) hoverRing.gameObject.SetActive(false);
        }

        public bool Open => root != null && root.activeSelf;

        public void Show(MysterySession s, Evidence e, Sprite sprite)
        {
            _evidence = e.Id;
            if (root != null) root.SetActive(true);
            if (art != null) { art.sprite = sprite; art.enabled = sprite != null; art.preserveAspect = true; }
            if (nameText != null) nameText.text = e.Name;
            if (sourceText != null) sourceText.text = e.Source;
            if (bodyText != null) bodyText.text = e.Text;
            foreach (var b in _spots) if (b != null) Destroy(b.gameObject);
            _spots.Clear();
            if (detailTemplate != null && detailLayer != null)
            {
                var size = detailLayer.rect.size;
                foreach (var d in e.Details)
                {
                    var b = Instantiate(detailTemplate, detailLayer);
                    b.gameObject.SetActive(true);
                    var rt = (RectTransform)b.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                    rt.pivot = new Vector2(0, 1);
                    rt.anchoredPosition = new Vector2(d.X * size.x, -d.Y * size.y);
                    rt.sizeDelta = new Vector2(d.W * size.x, d.H * size.y);
                    string id = d.Id;
                    b.onClick.AddListener(() => DetailClicked?.Invoke(_evidence, id));
                    var relay = b.GetComponent<HoverRelay>();
                    if (relay != null)
                    {
                        relay.Entered += r => ShowRing((RectTransform)r.transform);
                        relay.Exited += _ => { if (hoverRing != null) hoverRing.gameObject.SetActive(false); };
                    }
                    _spots.Add(b);
                }
            }
            Refresh(s, e);
        }

        public void Refresh(MysterySession s, Evidence e)
        {
            var found = e.Details.Where(d => s.DetailFound(d.Id)).ToList();
            if (foundText != null) foundText.text = string.Join("\n\n", found.Select(d => $"<b>◆ {d.Label}</b>\n{d.Text}"));
            if (hintText != null) hintText.text = e.Details.Length == 0 ? hintNone : found.Count == e.Details.Length ? hintAllFound : hintSearching;
            for (int i = 0; i < _spots.Count && i < e.Details.Length; i++)
            {
                var mark = _spots[i].transform.Find("Found");
                if (mark != null) mark.gameObject.SetActive(s.DetailFound(e.Details[i].Id));
            }
        }

        void ShowRing(RectTransform spot)
        {
            if (hoverRing == null) return;
            hoverRing.gameObject.SetActive(true);
            hoverRing.anchoredPosition = spot.anchoredPosition + new Vector2(spot.sizeDelta.x * 0.5f, -spot.sizeDelta.y * 0.5f);
            hoverRing.sizeDelta = new Vector2(spot.sizeDelta.x * 1.35f, spot.sizeDelta.y * 1.25f);
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
            if (hoverRing != null) hoverRing.gameObject.SetActive(false);
            Closed?.Invoke();
        }
    }
}
