using System;
using System.Collections;
using UnityEngine;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 잼 도트 캐릭터(왼쪽을 보는 3/4 측면 — 잼 SpriteMansionView도 오른쪽으로 갈 때 뒤집는다)를 걷기·서 있기 프레임으로 돌린다.
    /// 걷기 그림은 '걸은 거리'로 넘긴다: 한 주기(walkStride 유닛)를 걸으면 걷기 그림 한 바퀴 — 딛는 발이 바닥에 붙어 미끄러지지 않는다.
    /// 움직임은 바깥(탐정 조종기 · 손님 감독)이 정하고, 여기선 그림과 걸음 박자만 맡는다.
    /// </summary>
    public class PixelActor : MonoBehaviour
    {
        public string actorName;
        public SpriteRenderer body;
        public Sprite[] idle;
        public Sprite[] walk;
        public float idleFps = 8f;
        public float walkStride = 1.2f;          // 걷기 그림 한 바퀴(두 걸음)에 나아가는 거리(월드 단위)
        public float speed = 2.4f;               // 걷는 속도(월드 단위/초)
        public float pixelSnap = 0.01f;          // 그림을 화면 1픽셀 격자에 맞춰 그린다(카메라가 있으면 그 해상도로, 없으면 이 값) — 0이면 끔
        public SpriteRenderer shadow;            // 발밑 그림자(평평한 스프라이트를 바닥에 붙인다)
        public GameObject nameTag;               // 저택도에서만 보이는 이름표

        public event Action Step;                // 발이 바닥에 닿는 순간(걸음 소리)

        bool _moving;
        bool _wantMoving;
        float _stillTime;
        float _idleClock;
        float _walkDist;
        float _lastX;
        int _lastWalkFrame = -1;
        Coroutine _walk;

        public bool FacingRight { get; private set; } = true;
        public bool Moving => _moving;

        void Awake()
        {
            _idleClock = UnityEngine.Random.value * 3f;   // 모두가 같은 박자로 숨 쉬지 않게
            _lastX = transform.position.x;
            if (shadow != null) _shadowY = shadow.transform.localPosition.y;
        }

        /// <summary>그림은 왼쪽을 본다 — 오른쪽을 볼 때 뒤집는다.</summary>
        public void SetFacing(bool right)
        {
            FacingRight = right;
            if (body != null) body.flipX = right;
        }

        public void Face(float worldX)
        {
            if (Mathf.Abs(worldX - transform.position.x) > 0.05f) SetFacing(worldX > transform.position.x);
        }

        /// <summary>걷는 중인지. 한두 프레임 멈춘다고 바로 서는 자세로 돌아가지 않는다(pace는 예전 호환용 — 박자는 걸은 거리가 정한다).</summary>
        public void SetMoving(bool moving, float pace = 1f)
        {
            _wantMoving = moving;
            if (moving && !_moving)
            {
                _moving = true;
                _stillTime = 0f;
                _walkDist = 0f;            // 첫 그림 = 앞발 착지 — 걷기 시작하자마자 한 걸음 소리
                _lastWalkFrame = -1;
            }
        }

        public void Place(Vector3 p)
        {
            StopWalk();
            transform.position = p;
            _lastX = p.x;
            _wantMoving = false;
            _moving = false;
        }

        // 모든 이동(조종기 Update · 걷기 코루틴)이 끝난 뒤에 그림을 고르고 그릴 자리를 맞춘다 — 한 프레임 늦은 위치로 맞추면 오히려 떨린다
        void LateUpdate()
        {
            float x = transform.position.x;
            float moved = Mathf.Abs(x - _lastX);
            _lastX = x;
            if (moved > 1f) moved = 0f;   // 순간 이동(계단·저택도 이동)은 걸음이 아니다

            if (_moving && !_wantMoving)
            {
                _stillTime += Time.deltaTime;
                if (_stillTime > 0.09f) { _moving = false; _lastWalkFrame = -1; }
            }
            else _stillTime = 0f;

            if (body != null)
            {
                if (_moving && walk != null && walk.Length > 0)
                {
                    _walkDist += moved;
                    int n = walk.Length;
                    int f = Mathf.FloorToInt(_walkDist / Mathf.Max(0.05f, walkStride) * n) % n;
                    body.sprite = walk[f];
                    // 발이 닿는 그림(첫 그림 · 반 바퀴)에 들어설 때 걸음 소리
                    if (f != _lastWalkFrame && (f == 0 || f == n / 2)) Step?.Invoke();
                    _lastWalkFrame = f;
                }
                else if (idle != null && idle.Length > 0)
                {
                    _idleClock += Time.deltaTime;
                    body.sprite = idle[Mathf.FloorToInt(_idleClock * idleFps) % idle.Length];
                }
            }
            SnapBody();
        }

        static Camera _cam;
        float _shadowY;

        /// <summary>몸 그림만 화면 픽셀 격자에 붙인다(실제 위치는 매끄럽게 움직이고, 그려지는 자리만 격자에 맞춘다).</summary>
        void SnapBody()
        {
            if (pixelSnap <= 0f || body == null) return;
            if (_cam == null) _cam = Camera.main;
            float unit = _cam != null && _cam.orthographic && _cam.pixelHeight > 0 ? 2f * _cam.orthographicSize / _cam.pixelHeight : pixelSnap;
            var p = transform.position;
            var snapped = new Vector3(Mathf.Round(p.x / unit) * unit, Mathf.Round(p.y / unit) * unit, p.z);
            var local = transform.InverseTransformVector(snapped - p);
            body.transform.localPosition = new Vector3(local.x, local.y, 0f);
            if (shadow != null) shadow.transform.localPosition = new Vector3(local.x, local.y + _shadowY, 0f);
        }

        // ───────────── 손님용: 목표 지점까지 걷기 ─────────────

        public bool Walking => _walk != null;

        public void WalkTo(float x, float speedMul = 1f, Action done = null)
        {
            StopWalk();
            _walk = StartCoroutine(WalkRoutine(x, speedMul, done));
        }

        public void StopWalk()
        {
            if (_walk != null) StopCoroutine(_walk);
            _walk = null;
            SetMoving(false);
        }

        IEnumerator WalkRoutine(float x, float speedMul, Action done)
        {
            SetMoving(true);
            SetFacing(x > transform.position.x);
            while (Mathf.Abs(transform.position.x - x) > 0.02f)
            {
                var p = transform.position;
                p.x = Mathf.MoveTowards(p.x, x, speed * speedMul * Time.deltaTime);
                transform.position = p;
                SetMoving(true);
                yield return null;
            }
            SetMoving(false);
            _walk = null;
            done?.Invoke();
        }

        public IEnumerator Fade(float to, float seconds)
        {
            if (body == null) yield break;
            float from = body.color.a;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                SetAlpha(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            SetAlpha(to);
        }

        public void SetAlpha(float a)
        {
            if (body == null) return;
            var c = body.color;
            body.color = new Color(c.r, c.g, c.b, a);
            if (shadow != null) { var s = shadow.color; shadow.color = new Color(s.r, s.g, s.b, a * 0.55f); }
        }
    }
}
