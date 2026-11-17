using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MafiaAI.Alibi.UI;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 저택 단면도(인형의 집처럼 앞벽을 걷어 낸 3층 건물)의 공간 정보. 빌더가 씬에 저작해 둔 값을 담는다.
    /// 방 상자·바닥 높이·조사 지점·계단의 월드 좌표를 알려 주고, 지금 있는 방만 밝게(나머지는 어둡게) 비춘다.
    /// </summary>
    public class ManorWorld : MonoBehaviour
    {
        [Serializable]
        public class HotspotSlot
        {
            public string id;
            public Rect rect;            // 월드 좌표 사각형
        }

        [Serializable]
        public class StairSlot
        {
            public string to;
            public float x;              // 월드 x
            public bool up;
        }

        [Serializable]
        public class OverlaySlot
        {
            public string showIf;
            public GameObject go;
        }

        [Serializable]
        public class RoomSlot
        {
            public string id;
            public Rect box;             // 방 안쪽 월드 사각형
            public float floorY;         // 발이 닿는 높이
            public SpriteRenderer backdrop;
            public SpriteRenderer dim;   // 다른 방에 있을 때 덮는 어둠
            public HotspotSlot[] hotspots;
            public StairSlot[] stairs;
            public OverlaySlot[] overlays;
            public GameObject label;     // 저택도(M)에서만 보이는 방 이름
        }

        public RoomSlot[] rooms;
        public Rect houseBounds;         // 카메라가 따라갈 수 있는 범위
        public float roomWidth = 10f;    // 한 칸 너비(걸음 → 시간 환산)
        public float doorMargin = 0.55f; // 벽에 붙어 서는 여유
        [Range(0f, 1f)] public float dimOther = 0.62f;
        [Range(0f, 1f)] public float dimOverview = 0.18f;

        readonly Dictionary<string, Coroutine> _dimCo = new Dictionary<string, Coroutine>();

        public RoomSlot Room(string id) => rooms.FirstOrDefault(r => r.id == id);

        public RoomSlot RoomAt(Vector2 p) => rooms.FirstOrDefault(r => p.x >= r.box.xMin - 0.2f && p.x <= r.box.xMax + 0.2f && p.y >= r.box.yMin - 0.3f && p.y <= r.box.yMax);

        public Vector3 FloorPoint(string roomId, float u)
        {
            var r = Room(roomId);
            return new Vector3(Mathf.Lerp(r.box.xMin, r.box.xMax, Mathf.Clamp01(u)), r.floorY, 0f);
        }

        public Vector3 Center(string roomId)
        {
            var r = Room(roomId);
            return new Vector3(r.box.center.x, r.box.center.y, 0f);
        }

        public HotspotSlot Hotspot(string roomId, string hotspotId) => Room(roomId)?.hotspots?.FirstOrDefault(h => h.id == hotspotId);

        /// <summary>같은 층 옆방으로 이어지는 문(방향 -1 왼쪽 / +1 오른쪽).</summary>
        public string Neighbor(MysteryCase c, string roomId, int dir)
        {
            var me = c.RoomById(roomId);
            foreach (var cor in c.Corridors)
            {
                string other = cor[0] == roomId ? cor[1] : cor[1] == roomId ? cor[0] : null;
                if (other == null) continue;
                var o = c.RoomById(other);
                if (o.Y == me.Y && o.X == me.X + dir) return other;
            }
            return null;
        }

        /// <summary>지금 방만 밝게, 나머지는 어둡게(overview면 전부 거의 밝게).</summary>
        public void Focus(string roomId, bool overview, float seconds = 0.5f)
        {
            foreach (var r in rooms)
            {
                if (r.dim == null) continue;
                float target = overview ? (r.id == roomId ? 0f : dimOverview) : (r.id == roomId ? 0f : dimOther);
                if (_dimCo.TryGetValue(r.id, out var co) && co != null) StopCoroutine(co);
                if (!isActiveAndEnabled || seconds <= 0f) { SetDim(r, target); continue; }
                _dimCo[r.id] = StartCoroutine(DimTo(r, target, seconds));
            }
        }

        static void SetDim(RoomSlot r, float a)
        {
            var c = r.dim.color;
            r.dim.color = new Color(c.r, c.g, c.b, a);
        }

        IEnumerator DimTo(RoomSlot r, float target, float seconds)
        {
            float from = r.dim.color.a;
            yield return AlibiFx.Tween(seconds, k => SetDim(r, Mathf.Lerp(from, target, AlibiFx.EaseInOut(k))));
        }

        public void ShowLabels(bool on)
        {
            foreach (var r in rooms) if (r.label != null) r.label.SetActive(on);
        }

        /// <summary>조건부로 보이는 그림(두 번째 살인 뒤 온실의 흰 천 등)을 갱신한다.</summary>
        public void RefreshOverlays(MysterySession s)
        {
            foreach (var r in rooms)
            {
                if (r.overlays == null) continue;
                foreach (var o in r.overlays)
                    if (o.go != null) o.go.SetActive(s.Eval(o.showIf));
            }
        }
    }
}
