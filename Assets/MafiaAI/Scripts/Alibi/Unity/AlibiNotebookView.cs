using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.Alibi.UI
{
    /// <summary>
    /// 오른쪽 수사 수첩: 손글씨 진술표 + 잉크로 그린 저택 도면 + 모순 제시 인장.
    /// 진술표의 칸·도면의 방은 씬에 저작돼 있고, 이 스크립트는 글자 내용·상태 색·표시 여부만 바꾼다.
    /// </summary>
    public class AlibiNotebookView : MonoBehaviour
    {
        [Serializable]
        public class CellUI
        {
            public Button button;
            public RectTransform rect;
            public TMP_Text room;
            public TMP_Text seen;
            public Image circle;      // 고른 칸: 붉은 색연필 동그라미
            public TMP_Text mark;     // 거짓 · ✓ · ! 같은 붓 표시
        }

        [Serializable]
        public class RowUI
        {
            public Button header;
            public Image face;
            public TMP_Text name;
            public TMP_Text status;
            public Image strike;      // 숨진 사람: 줄 전체를 붉게 긋는다
            public CellUI[] cells = new CellUI[4];
        }

        [Serializable]
        public class RoomUI
        {
            public string room;
            public Image box;
            public Image fill;
            public TMP_Text label;
            public Image weapon;
            public TMP_Text occupants;
            public Image murderMark;
        }

        [Serializable]
        public class CorridorUI
        {
            public string a;
            public string b;
            public Image image;
        }

        [Header("진술표")]
        public RowUI[] rows = new RowUI[5];
        public Button[] slotButtons = new Button[4];
        public TMP_Text[] slotLabels = new TMP_Text[4];
        public Image[] slotMarks = new Image[4];   // 지도에 보이는 시각 표시(밑줄)

        [Header("도면")]
        public RoomUI[] rooms;
        public CorridorUI[] corridors;
        public TMP_Text focusLabel;
        public TMP_Text distance;

        [Header("하단")]
        public TMP_Text selection;
        public Button presentButton;
        public CanvasGroup presentGroup;
        public Button clearButton;
        public Button accuseButton;

        [Header("색")]
        public Color ink = new Color(0.17f, 0.12f, 0.09f);
        public Color pencil = new Color(0.55f, 0.48f, 0.4f);
        public Color crimson = new Color(0.62f, 0.1f, 0.17f);
        public Color evidenceInk = new Color(0.12f, 0.27f, 0.42f);   // 고백·수첩으로 새로 적힌 칸(파란 잉크)
        public Color faceDead = new Color(0.45f, 0.43f, 0.43f);
        public Color roomFill = new Color(0f, 0f, 0f, 0f);
        public Color murderFill = new Color(0.62f, 0.08f, 0.12f, 0.16f);
        public Color selectAFill = new Color(0.85f, 0.55f, 0.15f, 0.28f);
        public Color selectBFill = new Color(0.45f, 0.3f, 0.65f, 0.25f);
        public Color corridorInk = new Color(0.17f, 0.12f, 0.09f, 0.85f);
        public Color pathOk = new Color(0.2f, 0.5f, 0.28f, 1f);
        public Color pathBad = new Color(0.75f, 0.1f, 0.15f, 1f);

        [Header("표기(내용 형식)")]
        public string unknownText = "—";
        public string seenUnknownText = "누굴 봤나?";
        public string seenNoneText = "아무도 못 봄";
        public string seenFormat = "본: {0}";
        public string victimSuffix = "†";
        public string correctedFormat = "<s>{0}</s> {1}";
        public string murderSlotFormat = "{0}\n<size=70%><color=#9E1B30>비명</color></size>";
        public string evadedMark = "거\n짓";   // 칸 오른쪽 기둥에 세로로 찍는 낙관
        public string maintainedMark = "✓";
        public string contradictionMark = "!";
        public string statusOpen = "심문 중";
        public string statusEvaded = "해명 실패";
        public string statusConfessed = "비밀 고백";
        public string statusDead = "† 사망";
        public string focusFormat = "<color=#9E1B30>{0}</color>에 여기 있었다는 사람";
        public string distanceHint = "진술 두 칸을 고르면 두 방 사이 거리를 잰다.";
        public string distanceFormat = "{0} → {1} · 복도 {2}칸, 최소 {3}분 · 두 진술 사이 {4}분";
        public string distanceSameRoom = "둘 다 {0} — 같은 방이다. 서로 봤어야 한다.";
        public string distanceBad = "  <color=#9E1B30>갈 수 없다</color>";
        public string distanceOk = "  갈 수 있다";
        public string selectionNone = "의심 가는 진술 두 칸을 골라라.";
        public string selectionOne = "{0} — 하나 더.";
        public string selectionTwo = "{0}  ×  {1}";
        public string cellFormat = "{0} {1}";

        public event Action<int, int> CellClicked;
        public event Action<int> RowClicked;
        public event Action<int> SlotClicked;
        public event Action PresentClicked, ClearClicked, AccuseClicked;

        void Awake()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                int ri = i;
                var row = rows[i];
                if (row == null) continue;
                if (row.header != null) row.header.onClick.AddListener(() => RowClicked?.Invoke(ri));
                for (int t = 0; t < row.cells.Length; t++)
                {
                    int ti = t;
                    if (row.cells[t]?.button != null) row.cells[t].button.onClick.AddListener(() => CellClicked?.Invoke(ri, ti));
                }
            }
            for (int t = 0; t < slotButtons.Length; t++)
            {
                int ti = t;
                if (slotButtons[t] != null) slotButtons[t].onClick.AddListener(() => SlotClicked?.Invoke(ti));
            }
            if (presentButton != null) presentButton.onClick.AddListener(() => PresentClicked?.Invoke());
            if (clearButton != null) clearButton.onClick.AddListener(() => ClearClicked?.Invoke());
            if (accuseButton != null) accuseButton.onClick.AddListener(() => AccuseClicked?.Invoke());
        }

        public void Render(AlibiSession s, IReadOnlyList<CellRef> selected, int focusSlot, string openSuspect, Func<string, Sprite> faceOf, bool canPresent, bool canAccuse)
        {
            var c = s.Case;
            for (int t = 0; t < slotLabels.Length && t < c.SlotCount; t++)
            {
                string label = AlibiCase.TimeLabel(t);
                if (slotLabels[t] != null) slotLabels[t].text = t == AlibiCase.MurderSlot ? string.Format(murderSlotFormat, label) : label;
                if (slotMarks.Length > t && slotMarks[t] != null) slotMarks[t].enabled = t == focusSlot;
            }

            for (int i = 0; i < rows.Length && i < c.Suspects.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                var p = c.Suspects[i];
                bool evaded = s.Contradictions.Any(r => r.Reactions.TryGetValue(p.Name, out var re) && re == PressReaction.Evade);
                if (row.face != null) { row.face.sprite = faceOf?.Invoke(p.Name); row.face.color = p.Dead ? faceDead : Color.white; }
                if (row.name != null) { row.name.text = p.Name; row.name.color = p.Dead ? pencil : ink; }
                if (row.status != null)
                    row.status.text = p.Dead ? statusDead : p.Name == openSuspect ? statusOpen : evaded ? statusEvaded : p.Confessed ? statusConfessed : "";
                if (row.strike != null) row.strike.enabled = p.Dead;

                for (int t = 0; t < row.cells.Length && t < c.SlotCount; t++)
                {
                    var ui = row.cells[t];
                    if (ui == null) continue;
                    var st = s.Cell(i, t);
                    bool sel = selected.Any(x => x.Person == p.Name && x.Slot == t);
                    bool corrected = st.CorrectedFrom != null;
                    if (ui.room != null)
                    {
                        ui.room.text = !st.LocationKnown ? unknownText : corrected ? string.Format(correctedFormat, st.CorrectedFrom, st.Room) : st.Room;
                        ui.room.color = !st.LocationKnown ? pencil : corrected ? evidenceInk : ink;
                    }
                    if (ui.seen != null)
                    {
                        ui.seen.text = !st.LocationKnown ? ""
                            : !st.SeenKnown ? (p.Dead ? "" : seenUnknownText)   // 죽은 사람에겐 더 물을 수 없다
                            : st.Seen.Count == 0 ? seenNoneText
                            : string.Format(seenFormat, string.Join(", ", st.Seen.Select(n => n == c.Victim ? n + victimSuffix : n)));
                        ui.seen.color = st.SeenKnown ? (corrected ? evidenceInk : ink) : pencil;
                    }
                    if (ui.circle != null) ui.circle.enabled = sel;
                    if (ui.mark != null)
                    {
                        ui.mark.text = st.Evaded ? evadedMark : st.Maintained ? maintainedMark : st.InContradiction ? contradictionMark : "";
                        ui.mark.color = st.Maintained && !st.Evaded ? ink : crimson;
                    }
                    if (ui.button != null) ui.button.interactable = !s.Over;
                }
            }

            RenderPlan(s, selected, focusSlot);

            if (selection != null)
            {
                string Cell(CellRef r) => string.Format(cellFormat, r.Person, AlibiCase.TimeLabel(r.Slot));
                selection.text = selected.Count == 0 ? selectionNone
                    : selected.Count == 1 ? string.Format(selectionOne, Cell(selected[0]))
                    : string.Format(selectionTwo, Cell(selected[0]), Cell(selected[1]));
            }
            if (presentButton != null) presentButton.interactable = canPresent;
            if (presentGroup != null) presentGroup.alpha = canPresent ? 1f : 0.45f;
            if (clearButton != null) clearButton.interactable = selected.Count > 0 && !s.Over;
            if (accuseButton != null) accuseButton.interactable = canAccuse;
        }

        void RenderPlan(AlibiSession s, IReadOnlyList<CellRef> selected, int focusSlot)
        {
            var c = s.Case;
            if (focusLabel != null) focusLabel.text = string.Format(focusFormat, AlibiCase.TimeLabel(focusSlot));

            var occ = new Dictionary<string, List<string>>();
            for (int i = 0; i < c.Suspects.Count; i++)
            {
                var st = s.Cell(i, focusSlot);
                if (!st.LocationKnown) continue;
                if (!occ.TryGetValue(st.Room, out var list)) occ[st.Room] = list = new List<string>();
                list.Add(c.Suspects[i].Name);
            }

            string roomA = selected.Count > 0 && s.IsKnown(selected[0]) ? s.View(selected[0]).Room : null;
            string roomB = selected.Count > 1 && s.IsKnown(selected[1]) ? s.View(selected[1]).Room : null;
            foreach (var r in rooms)
            {
                if (r == null) continue;
                if (r.label != null) r.label.text = r.room;
                if (r.occupants != null) r.occupants.text = occ.TryGetValue(r.room, out var names) ? string.Join(" · ", names) : "";
                if (r.murderMark != null) r.murderMark.enabled = r.room == c.MurderRoom;
                if (r.fill != null)
                    r.fill.color = r.room == roomA ? selectAFill : r.room == roomB ? selectBFill : r.room == c.MurderRoom ? murderFill : roomFill;
            }
            foreach (var cor in corridors) if (cor?.image != null) cor.image.color = corridorInk;

            if (distance == null) return;
            if (roomA == null || roomB == null) { distance.text = distanceHint; return; }
            if (roomA == roomB) { distance.text = string.Format(distanceSameRoom, roomA); return; }
            var path = AlibiMap.ShortestPath(roomA, roomB);
            int d = path.Count - 1;
            int gap = Math.Abs(selected[0].Slot - selected[1].Slot) * ContradictionRules.MinutesPerSlot;
            bool bad = d * ContradictionRules.MinutesPerSlot > gap;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var cor = corridors.FirstOrDefault(x => x != null && ((x.a == path[i] && x.b == path[i + 1]) || (x.b == path[i] && x.a == path[i + 1])));
                if (cor?.image != null) cor.image.color = bad ? pathBad : pathOk;
            }
            distance.text = string.Format(distanceFormat, roomA, roomB, d, d * ContradictionRules.MinutesPerSlot, gap) + (bad ? distanceBad : distanceOk);
        }

        // ───────────── 연출 ─────────────

        /// <summary>새로 적힌 칸이 톡 튀어나온다(무엇이 바뀌었는지 눈이 따라가게).</summary>
        public IEnumerator Pulse(IEnumerable<(int Row, int Slot)> cells)
        {
            var list = cells.Where(x => x.Row < rows.Length && rows[x.Row] != null && x.Slot < rows[x.Row].cells.Length)
                .Select(x => rows[x.Row].cells[x.Slot]?.rect).Where(r => r != null).ToList();
            if (list.Count == 0) yield break;
            yield return AlibiFx.Tween(0.45f, k =>
            {
                float sc = 1f + 0.12f * Mathf.Sin(k * Mathf.PI);
                foreach (var r in list) r.localScale = Vector3.one * sc;
            });
            foreach (var r in list) r.localScale = Vector3.one;
        }

        /// <summary>붓 표시가 도장처럼 찍힌다.</summary>
        public IEnumerator Stamp(int row, int slot)
        {
            if (row >= rows.Length || rows[row] == null || slot >= rows[row].cells.Length) yield break;
            var mark = rows[row].cells[slot]?.mark;
            if (mark == null) yield break;
            yield return AlibiFx.Slam(mark.rectTransform, 2.4f, 0.35f, -20f);
        }
    }
}
