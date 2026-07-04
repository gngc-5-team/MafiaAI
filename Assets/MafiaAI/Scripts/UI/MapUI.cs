using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 매판 랜덤 생성된 저택 구조(MansionGenerator.Layout)를 작은 도식 지도로 그린다.
    /// 방/복도 개수가 게임마다 달라서(GameConfig.RoomCount), room1..N 스프라이트처럼
    /// 코드에서 Instantiate로 필요한 만큼 찍어낸다. UI는 하이라키(패널)+프리팹 2개만 준비하면 됨.
    /// </summary>
    public class MapUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] RectTransform mapRoot;            // 방/복도가 그려질 빈 컨테이너(패널 크기만큼)
        [SerializeField] RectTransform roomNodePrefab;      // 방 하나(원 + 이름 텍스트)
        [SerializeField] RectTransform corridorLinePrefab;  // 복도 하나(가운데 피벗의 얇고 긴 이미지)
        [SerializeField] Button toggleButton;               // 선택: 버튼으로도 켜고 끄고 싶으면 연결(M키로도 가능)

        [SerializeField] float cellSize = 44f;              // 방 격자 한 칸의 기본 UI 간격(px)
        [SerializeField] float fillRatio = 0.8f;            // 패널 안에 다 들어오도록 줄일 때 쓰는 여유 비율
        [SerializeField] Color roomColor = new Color32(0x2A, 0x2A, 0x35, 0xFF);
        [SerializeField] Color myRoomColor = new Color32(0x9B, 0xE6, 0x6D, 0xFF);
        [SerializeField] Color corridorColor = new Color32(0x55, 0x55, 0x60, 0xFF);

        readonly Dictionary<string, RectTransform> _roomNodes = new();
        readonly List<RectTransform> _spawned = new();

        void Reset() => controller = GetComponent<GameController>();

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (toggleButton != null) toggleButton.onClick.AddListener(ToggleMap);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame && !IsTyping()) ToggleMap();
        }

        void ToggleMap()
        {
            if (mapRoot == null) return;
            mapRoot.gameObject.SetActive(!mapRoot.gameObject.activeSelf);
        }

        bool IsTyping()
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnGameSetup += BuildMap;
            controller.OnLocationsChanged += RefreshHighlight;
            controller.OnPhaseChanged += HandlePhaseChanged;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnGameSetup -= BuildMap;
            controller.OnLocationsChanged -= RefreshHighlight;
            controller.OnPhaseChanged -= HandlePhaseChanged;
        }

        void HandlePhaseChanged(GameState s) => RefreshHighlight();

        void BuildMap()
        {
            if (mapRoot == null || roomNodePrefab == null || corridorLinePrefab == null) return;

            foreach (var rt in _spawned) if (rt != null) Destroy(rt.gameObject);
            _spawned.Clear();
            _roomNodes.Clear();

            var mansion = controller.Mansion;
            if (mansion.GridPos == null || mansion.GridPos.Count == 0) return;

            // 방 개수/모양이 매판 다르니, 패널 크기에 맞춰 칸 간격을 자동으로 줄인다(넘치면 축소, 넉넉하면 기본값 유지).
            int minX = mansion.GridPos.Values.Min(p => p.X), maxX = mansion.GridPos.Values.Max(p => p.X);
            int minY = mansion.GridPos.Values.Min(p => p.Y), maxY = mansion.GridPos.Values.Max(p => p.Y);
            int spanX = Mathf.Max(1, maxX - minX), spanY = Mathf.Max(1, maxY - minY);
            var panelSize = mapRoot.rect.size;
            float fitCell = Mathf.Min(panelSize.x * fillRatio / spanX, panelSize.y * fillRatio / spanY);
            float cell = Mathf.Min(cellSize, fitCell > 0f ? fitCell : cellSize);
            Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            Vector2 ToLocal((int X, int Y) g) => (new Vector2(g.X, g.Y) - center) * cell;

            // 복도(선)를 방 노드보다 먼저 그려서 노드 아래에 깔리게 한다.
            foreach (var edge in mansion.Corridors)
            {
                if (!mansion.GridPos.TryGetValue(edge.A, out var ca) || !mansion.GridPos.TryGetValue(edge.B, out var cb)) continue;
                var line = Instantiate(corridorLinePrefab, mapRoot);
                _spawned.Add(line);

                Vector2 pa = ToLocal(ca), pb = ToLocal(cb);
                line.anchoredPosition = (pa + pb) * 0.5f;
                line.sizeDelta = new Vector2(Vector2.Distance(pa, pb), line.sizeDelta.y);
                line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
                var lineImg = line.GetComponent<Image>();
                if (lineImg != null) lineImg.color = corridorColor;
            }

            foreach (var room in controller.Rooms)
            {
                if (!mansion.GridPos.TryGetValue(room, out var roomCell)) continue;
                var node = Instantiate(roomNodePrefab, mapRoot);
                _spawned.Add(node);
                node.anchoredPosition = ToLocal(roomCell);

                var label = node.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = room;

                _roomNodes[room] = node;
            }

            RefreshHighlight();
        }

        void RefreshHighlight()
        {
            if (controller.HumanPlayer == null) return;
            string myRoom = controller.GetPlayerRoom(controller.HumanPlayer.Id);
            foreach (var kv in _roomNodes)
            {
                var img = kv.Value.GetComponent<Image>();
                if (img == null) continue;
                img.color = kv.Key == myRoom ? myRoomColor : roomColor;
            }
        }
    }
}
