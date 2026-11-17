using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 손님들을 동선표(MysteryCase.Schedule)대로 저택 안에 세운다. 시계가 넘어가 다른 방으로 옮길 때가 되면
    /// 문 쪽으로 걸어 나가 사라졌다가 새 방에 나타나 제자리로 걸어간다. 탐정이 가까이 오면 그쪽을 돌아본다.
    /// </summary>
    public class NpcDirector : MonoBehaviour
    {
        public ManorWorld world;
        public PixelActor[] actors;
        public float lookRange = 3.2f;

        readonly Dictionary<string, string> _room = new Dictionary<string, string>();
        readonly Dictionary<string, float> _x = new Dictionary<string, float>();
        readonly Dictionary<string, Coroutine> _moves = new Dictionary<string, Coroutine>();
        Transform _detective;
        bool _staged;

        public PixelActor Actor(string name) => actors?.FirstOrDefault(a => a != null && a.actorName == name);

        public string RoomOf(string name) => _room.TryGetValue(name, out var r) ? r : null;

        public void Bind(Transform detective) => _detective = detective;

        /// <summary>지금 시각의 자리로 맞춘다(instant=true면 걷지 않고 바로).</summary>
        public void Sync(MysterySession s, bool instant)
        {
            if (_staged) return;
            foreach (var a in actors)
            {
                if (a == null) continue;
                string name = a.actorName;
                if (s.IsDead(name)) { Hide(a); _room[name] = s.LocationOf(name); continue; }
                string room = s.LocationOf(name);
                if (room == null) { Hide(a); continue; }
                if (s.IsHeld(name) && !instant && a.gameObject.activeSelf) continue;   // 마주 앉아 이야기하는 중
                float x = s.LocationX(name);
                bool sameRoom = _room.TryGetValue(name, out var cur) && cur == room;
                bool sameSpot = sameRoom && _x.TryGetValue(name, out var cx) && Mathf.Approximately(cx, x);
                if (sameSpot && !instant) continue;
                _room[name] = room;
                _x[name] = x;
                var target = world.FloorPoint(room, x);
                if (_moves.TryGetValue(name, out var co) && co != null) StopCoroutine(co);
                if (instant || !a.gameObject.activeSelf)
                {
                    a.gameObject.SetActive(true);
                    a.SetAlpha(1f);
                    a.Place(target);
                    continue;
                }
                _moves[name] = StartCoroutine(sameRoom ? WalkWithin(a, target.x) : Relocate(a, cur, room, target));
            }
        }

        /// <summary>컷신용: 동선과 상관없이 그 자리에 세운다(Unstage 전까지 Sync가 건드리지 않는다).</summary>
        public void Stage(string name, Vector3 pos, bool faceRight)
        {
            _staged = true;
            var a = Actor(name);
            if (a == null) return;
            if (_moves.TryGetValue(name, out var co) && co != null) StopCoroutine(co);
            a.gameObject.SetActive(true);
            a.SetAlpha(1f);
            a.Place(pos);
            a.SetFacing(faceRight);
            _room.Remove(name);
            _x.Remove(name);
        }

        public void Unstage() => _staged = false;

        /// <summary>말을 걸었을 때: 걷던 중이면 멈추고, 아직 다른 방에서 오는 중이면 이 방 자리에 바로 세운다.</summary>
        public void Settle(string name, MysterySession s)
        {
            var a = Actor(name);
            if (a == null) return;
            if (_moves.TryGetValue(name, out var co) && co != null) StopCoroutine(co);
            _moves.Remove(name);
            string room = s.LocationOf(name);
            var slot = room != null ? world.Room(room) : null;
            a.gameObject.SetActive(true);
            a.SetAlpha(1f);
            a.StopWalk();
            if (slot != null && !slot.box.Contains(new Vector2(a.transform.position.x, slot.box.center.y)))
                a.Place(world.FloorPoint(room, s.LocationX(name)));
            else if (slot != null) a.Place(new Vector3(a.transform.position.x, slot.floorY, 0f));
            _room[name] = room;
            _x[name] = s.LocationX(name);
        }

        static void Hide(PixelActor a)
        {
            a.StopWalk();
            a.gameObject.SetActive(false);
        }

        IEnumerator WalkWithin(PixelActor a, float x)
        {
            a.WalkTo(x, 0.85f);
            while (a.Walking) yield return null;
        }

        /// <summary>다른 방으로: 지금 방의 출구(문·계단) 쪽으로 걸어가 사라졌다가, 새 방 입구에서 나타나 제자리로.</summary>
        IEnumerator Relocate(PixelActor a, string from, string to, Vector3 target)
        {
            var fromRoom = from != null ? world.Room(from) : null;
            if (fromRoom != null)
            {
                float exitX = ExitX(fromRoom, to);
                a.WalkTo(exitX, 1f);
                float t = 0f;
                while (a.Walking && t < 7f) { t += Time.deltaTime; yield return null; }   // 손님 걸음은 보폭에 맞춰 느리다
                yield return a.Fade(0f, 0.35f);
            }
            var toRoom = world.Room(to);
            float enterX = toRoom != null ? ExitX(toRoom, from) : target.x;
            a.Place(new Vector3(enterX, target.y, 0f));
            yield return a.Fade(1f, 0.35f);
            a.WalkTo(target.x, 0.9f);
            while (a.Walking) yield return null;
        }

        /// <summary>그 방에서 other 쪽으로 나가는 자리(옆방이면 문 쪽 벽, 위·아래층이면 계단).</summary>
        float ExitX(ManorWorld.RoomSlot room, string other)
        {
            if (other != null && room.stairs != null)
            {
                var st = room.stairs.FirstOrDefault(s => s.to == other);
                if (st != null) return st.x;
            }
            var o = other != null ? world.Room(other) : null;
            if (o != null && Mathf.Abs(o.box.center.y - room.box.center.y) < 0.5f)
                return o.box.center.x < room.box.center.x ? room.box.xMin + 0.4f : room.box.xMax - 0.4f;
            return room.box.center.x;
        }

        void Update()
        {
            if (_detective == null || actors == null) return;
            foreach (var a in actors)
            {
                if (a == null || !a.gameObject.activeSelf || a.Walking) continue;
                float dx = _detective.position.x - a.transform.position.x;
                float dy = Mathf.Abs(_detective.position.y - a.transform.position.y);
                if (dy < 1.5f && Mathf.Abs(dx) < lookRange) a.SetFacing(dx > 0f);
            }
        }
    }
}
