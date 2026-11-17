using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MafiaAI.Alibi.UI;

namespace MafiaAI.Mystery.UI
{
    /// <summary>
    /// 방 조사 화면: 방 그림 위 보이지 않는 조사 지점(마우스를 올리면 붉은 색연필 동그라미와 이름표),
    /// 살핀 곳엔 압정, 아래엔 서술창과 저택 도면(눌러서 이동, 복도 한 칸 = 5분).
    /// 조사 지점 버튼은 저작해 둔 틀을 복제해서 데이터 좌표에 놓는다(방마다 개수가 달라서).
    /// </summary>
    public class RoomView : MonoBehaviour
    {
        [Serializable]
        public class MapNode
        {
            public string room;
            public Button button;
            public Image box;
            public TMP_Text label;
            public TMP_Text badge;      // 아직 안 살핀 곳 수
            public HoverRelay hover;
        }

        [Serializable]
        public class MapLink
        {
            public string a, b;
            public Image image;
        }

        public GameObject root;

        [Header("방 그림")]
        public Image roomImage;
        public RectTransform overlayLayer;
        public Image overlayTemplate;
        public RectTransform hotspotLayer;
        public Button hotspotTemplate;      // 자식 "Pin"(압정)
        public TMP_Text roomName;
        public RectTransform hoverRing;     // 붉은 색연필 동그라미
        public RectTransform hoverTag;
        public TMP_Text hoverLabel;
        public Button interrogateButton;

        [Header("서술창")]
        public TMP_Text descTitle;
        public TMP_Text descText;
        public AlibiTypewriter descTypewriter;
        public Button descButton;

        [Header("도면")]
        public MapNode[] nodes;
        public MapLink[] links;
        public TMP_Text moveHint;
        public Color nodeHere = new Color(0.62f, 0.11f, 0.19f, 1f);
        public Color nodeVisited = new Color(1f, 1f, 1f, 1f);
        public Color nodeUnvisited = new Color(0.86f, 0.8f, 0.7f, 1f);
        public Color linkColor = new Color(0.17f, 0.12f, 0.09f, 0.75f);
        public string hereHint = "지금 있는 곳";
        public string moveFormat = "{0}(으)로 이동 · {1}분";
        public string idleHint = "방을 누르면 그리로 간다 · 복도 한 칸 5분";

        public event Action<string> HotspotClicked;
        public event Action<string> MoveClicked;
        public event Action InterrogateClicked;
        public event Action DescriptionClicked;

        readonly List<Button> _spots = new List<Button>();
        readonly List<GameObject> _overlays = new List<GameObject>();
        readonly Dictionary<string, Button> _byId = new Dictionary<string, Button>();
        MysterySession _session;

        void Awake()
        {
            if (hotspotTemplate != null) hotspotTemplate.gameObject.SetActive(false);
            if (overlayTemplate != null) overlayTemplate.gameObject.SetActive(false);
            if (interrogateButton != null) interrogateButton.onClick.AddListener(() => InterrogateClicked?.Invoke());
            if (descButton != null) descButton.onClick.AddListener(() => DescriptionClicked?.Invoke());
            if (nodes != null)
                foreach (var n in nodes)
                {
                    var node = n;
                    if (node.button != null) node.button.onClick.AddListener(() => MoveClicked?.Invoke(node.room));
                    if (node.hover != null)
                    {
                        node.hover.Entered += _ => ShowMoveHint(node.room);
                        node.hover.Exited += _ => ShowMoveHint(null);
                    }
                }
            HideHover();
        }

        public bool Visible => root != null && root.activeSelf;
        public void SetVisible(bool on) { if (root != null) root.SetActive(on); }

        /// <summary>방을 그린다(조사 지점은 데이터 좌표에 복제해 놓는다).</summary>
        public void ShowRoom(MysterySession s, Func<string, Sprite> art)
        {
            _session = s;
            var room = s.CurrentRoom;
            var sprite = art?.Invoke(room.Image);
            if (roomImage != null) { roomImage.sprite = sprite; roomImage.enabled = sprite != null; }
            foreach (var o in _overlays) if (o != null) Destroy(o);
            _overlays.Clear();
            if (overlayTemplate != null && overlayLayer != null)
            {
                var osize = overlayLayer.rect.size;
                foreach (var ov in room.Overlays.Where(o => s.Eval(o.ShowIf)))
                {
                    var img = Instantiate(overlayTemplate, overlayLayer);
                    img.gameObject.SetActive(true);
                    img.sprite = art?.Invoke(ov.Image);
                    img.preserveAspect = true;
                    var ort = img.rectTransform;
                    ort.anchorMin = ort.anchorMax = new Vector2(0, 1);
                    ort.pivot = new Vector2(0, 1);
                    ort.anchoredPosition = new Vector2(ov.X * osize.x, -ov.Y * osize.y);
                    ort.sizeDelta = new Vector2(ov.W * osize.x, ov.H * osize.y);
                    _overlays.Add(img.gameObject);
                }
            }
            if (roomName != null) roomName.text = room.Name;
            foreach (var b in _spots) if (b != null) Destroy(b.gameObject);
            _spots.Clear();
            _byId.Clear();
            HideHover();
            if (hotspotTemplate == null || hotspotLayer == null) return;
            var size = hotspotLayer.rect.size;
            foreach (var h in s.VisibleHotspots(room.Id))
            {
                var b = Instantiate(hotspotTemplate, hotspotLayer);
                b.gameObject.SetActive(true);
                b.name = "Spot_" + h.Id;
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(h.X * size.x, -h.Y * size.y);
                rt.sizeDelta = new Vector2(h.W * size.x, h.H * size.y);
                string id = h.Id;
                b.onClick.AddListener(() => HotspotClicked?.Invoke(id));
                var relay = b.GetComponent<HoverRelay>();
                if (relay != null)
                {
                    relay.key = h.Label;
                    relay.Entered += r => ShowHover((RectTransform)r.transform, r.key);
                    relay.Exited += _ => HideHover();
                }
                _spots.Add(b);
                _byId[id] = b;
            }
            RefreshPins();
        }

        public void RefreshPins()
        {
            if (_session == null) return;
            foreach (var h in _session.VisibleHotspots(_session.Room))
            {
                if (!_byId.TryGetValue(h.Id, out var b) || b == null) continue;
                var pin = b.transform.Find("Pin");
                if (pin != null) pin.gameObject.SetActive(_session.Examined(h));
            }
        }

        public void SetInteractable(bool on)
        {
            foreach (var b in _spots) if (b != null) b.interactable = on;
            if (nodes != null) foreach (var n in nodes) if (n.button != null) n.button.interactable = on;
            if (interrogateButton != null) interrogateButton.interactable = on;
        }

        void ShowHover(RectTransform spot, string label)
        {
            if (hoverRing != null)
            {
                hoverRing.gameObject.SetActive(true);
                var c = spot.anchoredPosition + new Vector2(spot.sizeDelta.x * 0.5f, -spot.sizeDelta.y * 0.5f);
                hoverRing.anchoredPosition = c;
                hoverRing.sizeDelta = new Vector2(Mathf.Max(70f, spot.sizeDelta.x * 1.25f), Mathf.Max(48f, spot.sizeDelta.y * 1.2f));
            }
            if (hoverTag != null)
            {
                hoverTag.gameObject.SetActive(true);
                if (hoverLabel != null) hoverLabel.text = label;
                float x = spot.anchoredPosition.x + spot.sizeDelta.x * 0.5f;
                float y = spot.anchoredPosition.y + 28f;                              // 이름표(가운데 기준)를 지점 위에
                if (y > -24f) y = spot.anchoredPosition.y - spot.sizeDelta.y - 28f;   // 그림 위쪽 끝이면 아래에 단다
                hoverTag.anchoredPosition = new Vector2(Mathf.Clamp(x, 90f, hotspotLayer.rect.width - 90f), y);
            }
        }

        /// <summary>자동 검증·스크린샷용: 마우스를 올린 것처럼 보여 준다.</summary>
        public void PreviewHover(string hotspotId)
        {
            if (_byId.TryGetValue(hotspotId, out var b) && b != null)
                ShowHover((RectTransform)b.transform, b.GetComponent<HoverRelay>()?.key);
        }

        void HideHover()
        {
            if (hoverRing != null) hoverRing.gameObject.SetActive(false);
            if (hoverTag != null) hoverTag.gameObject.SetActive(false);
        }

        // ───────────── 서술창 ─────────────

        public void Describe(string title, string text, bool typewriter = true)
        {
            if (descTitle != null) descTitle.text = title ?? "";
            if (descTypewriter != null && typewriter) descTypewriter.Play(text ?? "");
            else if (descTypewriter != null) descTypewriter.Show(text ?? "");
            else if (descText != null) descText.text = text ?? "";
        }

        public bool Typing => descTypewriter != null && descTypewriter.IsPlaying;
        public void CompleteTyping() { if (descTypewriter != null) descTypewriter.Complete(); }

        // ───────────── 도면 ─────────────

        public void RenderMap(MysterySession s)
        {
            _session = s;
            if (nodes != null)
                foreach (var n in nodes)
                {
                    bool here = n.room == s.Room;
                    bool visited = s.Visited(n.room);
                    if (n.box != null) n.box.color = here ? nodeHere : visited ? nodeVisited : nodeUnvisited;
                    if (n.label != null) n.label.color = here ? new Color(0.98f, 0.94f, 0.86f) : new Color(0.17f, 0.12f, 0.09f, visited ? 1f : 0.75f);
                    int left = visited ? s.Unexamined(n.room) : 0;
                    if (n.badge != null) n.badge.text = left > 0 ? left.ToString() : "";
                    if (n.button != null) n.button.interactable = s.CanAct;
                }
            if (links != null)
                foreach (var l in links)
                    if (l.image != null) l.image.color = linkColor;
            ShowMoveHint(null);
        }

        void ShowMoveHint(string room)
        {
            if (moveHint == null || _session == null) return;
            if (room == null) { moveHint.text = idleHint; return; }
            if (room == _session.Room) { moveHint.text = hereHint; return; }
            var r = _session.Case.RoomById(room);
            moveHint.text = string.Format(moveFormat, r.Name, _session.MoveCost(room)).Replace("(으)로", Ro(r.Name));
        }

        static string Ro(string w)
        {
            char c = w[w.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return "로";
            int jong = (c - 0xAC00) % 28;
            return jong != 0 && jong != 8 ? "으로" : "로";
        }
    }
}
