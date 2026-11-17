using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 탐정 조종: A/D(←/→)로 걷고 Shift로 서두른다. 같은 층 옆방과 문으로 이어져 있으면 그대로 걸어 들어간다.
    /// 걸은 거리는 Walked로 알린다(시간 환산은 게임 진행자가 한다). 마우스로 고른 지점까지 알아서 걸어가기도 한다(멀면 서둘러서).
    /// 걷는 속도는 걷기 그림의 보폭과 맞춘다 — 그래야 발이 바닥에 붙는다(빠르게 하려면 보폭이 긴 그림이 필요하다).
    /// </summary>
    public class DetectiveController : MonoBehaviour
    {
        public PixelActor actor;
        public ManorWorld world;
        public float runMultiplier = 1.45f;     // Shift · 먼 곳 클릭: 서두르는 걸음
        public float autoHurryDistance = 4.5f;  // 클릭한 곳이 이보다 멀면 서둘러 간다
        public float acceleration = 34f;       // 월드 단위/초² — 출발·정지가 툭 끊기지 않게
        public bool inputEnabled = true;

        [Header("더미 모습(임시)")]
        [Tooltip("켜면 탐정을 동그라미로 그린다. 원래 스프라이트(PixelActor의 idle·walk)는 그대로 남아 있어, 끄면 바로 돌아온다(Play 중에도).")]
        public bool dummyLook = false;
        public Color dummyColor = new Color(0.95f, 0.72f, 0.38f);
        public float dummyDiameter = 1.1f;      // 월드 단위

        Sprite[] _origIdle, _origWalk;
        Sprite _dummy;
        bool _dummyApplied;
        [NonSerialized] public float editorAxis;   // 자동 점검용: 0이 아니면 A/D 대신 이 값으로 걷는다(키 입력과 같은 길)

        public event Action<string> CrossedDoor;   // 들어선 옆방
        public event Action<float> Walked;         // 이번 프레임에 걸은 거리(월드 단위)

        public string RoomId { get; private set; }

        MysteryCase _case;
        float _velocity;
        float? _autoTarget;
        Action _autoDone;
        float _autoSpeed = 1f;

        public void Init(MysteryCase c, string roomId, Vector3 pos)
        {
            _case = c;
            RoomId = roomId;
            actor.Place(pos);
        }

        /// <summary>계단·순간 이동 뒤 방을 바꿔 둔다.</summary>
        public void SetRoom(string roomId, Vector3 pos)
        {
            RoomId = roomId;
            Halt();
            actor.Place(pos);
        }

        public bool AutoWalking => _autoTarget.HasValue;

        /// <summary>그 자리까지 걸어간다. speedMul을 주지 않으면 멀 때만 서두른다.</summary>
        public void AutoWalk(float x, Action done, float speedMul = -1f)
        {
            if (speedMul <= 0f) speedMul = Mathf.Abs(x - actor.transform.position.x) > autoHurryDistance ? runMultiplier : 1f;
            _autoTarget = x;
            _autoDone = done;
            _autoSpeed = speedMul;
        }

        public void CancelAuto()
        {
            _autoTarget = null;
            _autoDone = null;
        }

        /// <summary>계단·순간 이동·대화로 멈출 때 남은 속도를 버린다.</summary>
        public void Halt()
        {
            CancelAuto();
            _velocity = 0f;
            actor.SetMoving(false);
        }

        static float KeyAxis()
        {
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v += 1f;
            return v;
        }

        static bool Running()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }

        void Start() => ApplyLook();

        /// <summary>더미(동그라미) ↔ 원래 도트를 바꾼다. 원래 그림 배열은 보관해 두었다가 그대로 돌려놓는다.</summary>
        void ApplyLook()
        {
            if (actor == null) return;
            if (dummyLook)
            {
                if (_origIdle == null) { _origIdle = actor.idle; _origWalk = actor.walk; }
                if (_dummy == null) _dummy = MakeDummy();
                int walkLen = _origWalk != null && _origWalk.Length > 0 ? _origWalk.Length : 8;   // 걸음 소리 박자는 그대로
                actor.idle = new[] { _dummy };
                actor.walk = new Sprite[walkLen];
                for (int i = 0; i < walkLen; i++) actor.walk[i] = _dummy;
                if (actor.body != null) actor.body.sprite = _dummy;
            }
            else if (_origIdle != null)
            {
                actor.idle = _origIdle;
                actor.walk = _origWalk;
            }
            _dummyApplied = dummyLook;
        }

        /// <summary>도트 동그라미(바닥에 발을 둔 피벗) — 보는 쪽에 작은 홈이 있어 방향을 알 수 있다(그림은 왼쪽을 보는 기준).</summary>
        Sprite MakeDummy()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "DetectiveDummy" };
            var px = new Color32[n * n];
            Color32 fill = dummyColor, rim = Color.Lerp(dummyColor, Color.black, 0.55f), hi = Color.Lerp(dummyColor, Color.white, 0.45f), clear = new Color32(0, 0, 0, 0);
            float c = (n - 1) * 0.5f, r = n * 0.5f - 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    Color32 col = d > r ? clear : d > r - 2f ? rim : fill;
                    if (d <= r - 2f && x < c - 2f && y > c + 2f && d > r - 7f) col = hi;          // 왼쪽 위 빛
                    if (d <= r && x < 7 && Mathf.Abs(y - c) < 3f) col = rim;                         // 보는 쪽(왼쪽) 홈
                    px[y * n + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply();
            float scale = actor.body != null ? Mathf.Max(0.01f, actor.body.transform.localScale.x) : 1f;
            float ppu = n / Mathf.Max(0.1f, dummyDiameter / scale);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0f), ppu);
        }

        void Update()
        {
            if (dummyLook != _dummyApplied) ApplyLook();   // 인스펙터에서 켜고 끄면 바로
            if (_case == null || world == null || actor == null) return;
            if (actor.Walking) return;   // 연출용 걷기(도입부 등)가 움직이는 중
            var room = world.Room(RoomId);
            if (room == null) return;

            float axis = inputEnabled ? (editorAxis != 0f ? editorAxis : KeyAxis()) : 0f;
            if (Mathf.Abs(axis) > 0.01f) CancelAuto();
            float speedMul = Running() ? runMultiplier : 1f;
            if (_autoTarget.HasValue)
            {
                float d = _autoTarget.Value - actor.transform.position.x;
                if (Mathf.Abs(d) < 0.06f)
                {
                    var done = _autoDone;
                    CancelAuto();
                    actor.SetMoving(false);
                    done?.Invoke();
                    return;
                }
                axis = Mathf.Sign(d);
                speedMul = _autoSpeed;
            }

            var p = actor.transform.position;
            float targetVel = axis * actor.speed * speedMul;
            _velocity = Mathf.MoveTowards(_velocity, targetVel, acceleration * Time.deltaTime);
            if (Mathf.Abs(axis) < 0.01f && Mathf.Abs(_velocity) < 0.05f) _velocity = 0f;
            float dx = _velocity * Time.deltaTime;
            if (_autoTarget.HasValue && Mathf.Abs(dx) > Mathf.Abs(_autoTarget.Value - p.x)) { dx = _autoTarget.Value - p.x; _velocity = 0f; }
            float x = p.x + dx;

            string left = world.Neighbor(_case, RoomId, -1);
            string right = world.Neighbor(_case, RoomId, +1);
            float minX = room.box.xMin + world.doorMargin;
            float maxX = room.box.xMax - world.doorMargin;
            if (left != null) minX = float.NegativeInfinity;
            if (right != null) maxX = float.PositiveInfinity;
            if (x < minX || x > maxX) _velocity = 0f;
            x = Mathf.Clamp(x, minX, maxX);

            // 칸막이 한가운데를 넘으면 옆방에 들어선 것
            if (left != null)
            {
                var lr = world.Room(left);
                if (x < (lr.box.xMax + room.box.xMin) * 0.5f) { RoomId = left; CrossedDoor?.Invoke(left); room = lr; }
            }
            if (right != null && RoomId != left)
            {
                var rr = world.Room(right);
                if (x > (room.box.xMax + rr.box.xMin) * 0.5f) { RoomId = right; CrossedDoor?.Invoke(right); room = rr; }
            }

            float y = Mathf.MoveTowards(p.y, room.floorY, 4f * Time.deltaTime);
            float walked = Mathf.Abs(x - p.x);
            actor.transform.position = new Vector3(x, y, p.z);
            bool moving = walked > 0.0005f;
            actor.SetMoving(moving, Mathf.Abs(_velocity) / actor.speed);
            if (moving) actor.SetFacing(x > p.x);
            if (walked > 0f) Walked?.Invoke(walked);
        }
    }
}
