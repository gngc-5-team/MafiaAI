using System.Collections;
using UnityEngine;
using MafiaAI.Alibi.UI;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 영화 카메라: 평소엔 탐정을 부드럽게 따라가며 걷는 쪽을 조금 더 비춘다(층 높이는 고정).
    /// 대화 땐 두 사람을 투샷으로, 저택도(M)에선 단면 전체를, 컷신에선 지정한 구도로 천천히 옮겨 간다.
    /// 아주 작은 손떨림과 번개 때 흔들림을 얹는다.
    /// 그려지는 자리는 화면 픽셀 격자에 맞춘다(속으로는 매끄럽게 움직이고, 찍는 자리만 1픽셀 단위) — 도트가 헤엄치지 않게.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class ManorCamera : MonoBehaviour
    {
        public enum Mode { Follow, Frame }

        public Camera cam;
        public Transform target;
        public float exploreSize = 5.4f;          // 평소 화면 반높이(월드 단위)
        public float lookAhead = 2.2f;
        public float followSharpness = 3.2f;
        public float verticalOffset = 2.5f;       // 카메라 중심 = 바닥 + 이만큼(바닥선이 화면 아래쪽 4/5쯤에 온다)
        public float sway = 0.035f;               // 손떨림
        public Rect bounds;                       // 따라가기 범위
        public bool pixelSnap = true;             // 화면 1픽셀 격자에 맞춰 찍는다

        public Mode CurrentMode { get; private set; } = Mode.Follow;

        Vector3 _framePos;
        float _frameSize;
        Vector3 _pos;                             // 손떨림·격자 맞춤 전의 매끄러운 자리
        bool _hasPos;
        float _floorY;
        float _shake;
        float _shakeTime;
        float _lookDir = 1f;
        Coroutine _move;

        public void SetFloor(float floorY) => _floorY = floorY;

        public void Follow()
        {
            CurrentMode = Mode.Follow;
            if (_move != null) StopCoroutine(_move);
            _move = null;
        }

        /// <summary>지정한 중심·크기로 옮겨 간다(seconds=0이면 즉시).</summary>
        public Coroutine FrameAt(Vector2 center, float size, float seconds)
        {
            CurrentMode = Mode.Frame;
            if (_move != null) StopCoroutine(_move);
            if (seconds <= 0f)
            {
                _framePos = new Vector3(center.x, center.y, transform.position.z);
                _frameSize = size;
                _pos = _framePos;
                _hasPos = true;
                transform.position = _framePos;
                cam.orthographicSize = size;
                return null;
            }
            _move = StartCoroutine(MoveTo(new Vector3(center.x, center.y, transform.position.z), size, seconds));
            return _move;
        }

        /// <summary>따라가기 자리로 즉시 맞춘다(방을 순간 이동한 뒤).</summary>
        public void Snap()
        {
            if (target == null) return;
            _pos = FollowPoint();
            _hasPos = true;
            transform.position = _pos;
            cam.orthographicSize = exploreSize;
        }

        IEnumerator MoveTo(Vector3 pos, float size, float seconds)
        {
            var p0 = _hasPos ? _pos : transform.position;
            float s0 = cam.orthographicSize;
            yield return AlibiFx.Tween(seconds, k =>
            {
                float e = AlibiFx.EaseInOut(k);
                _framePos = Vector3.Lerp(p0, pos, e);
                _frameSize = Mathf.Lerp(s0, size, e);
            });
            _framePos = pos;
            _frameSize = size;
            _move = null;
        }

        public void Shake(float amplitude, float seconds)
        {
            _shake = Mathf.Max(_shake, amplitude);
            _shakeTime = Mathf.Max(_shakeTime, seconds);
        }

        Vector3 FollowPoint()
        {
            var t = target.position;
            var actor = target.GetComponent<PixelActor>();
            if (actor != null) _lookDir = Mathf.Lerp(_lookDir, actor.FacingRight ? 1f : -1f, Time.deltaTime * 1.5f);
            float halfW = exploreSize * cam.aspect;
            float x = t.x + _lookDir * lookAhead;
            float y = _floorY + verticalOffset;
            if (bounds.width > 0f)
            {
                x = Mathf.Clamp(x, bounds.xMin + halfW, Mathf.Max(bounds.xMin + halfW, bounds.xMax - halfW));
            }
            return new Vector3(x, y, transform.position.z);
        }

        void LateUpdate()
        {
            if (cam == null) return;
            if (!_hasPos) { _pos = transform.position; _hasPos = true; }
            float size;
            if (CurrentMode == Mode.Follow && target != null)
            {
                float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
                _pos = Vector3.Lerp(_pos, FollowPoint(), k);
                size = Mathf.Lerp(cam.orthographicSize, exploreSize, k);
            }
            else
            {
                _pos = _framePos;
                size = _frameSize;
            }
            var goal = _pos;
            var off = SwayOffset();
            if (_shakeTime > 0f)
            {
                _shakeTime -= Time.deltaTime;
                off += (Vector3)(Random.insideUnitCircle * _shake);
                if (_shakeTime <= 0f) _shake = 0f;
            }
            var p = goal + off;
            if (pixelSnap)
            {
                float unit = 2f * size / Mathf.Max(1, cam.pixelHeight);
                p.x = Mathf.Round(p.x / unit) * unit;
                p.y = Mathf.Round(p.y / unit) * unit;
            }
            transform.position = p;
            cam.orthographicSize = size;
        }

        Vector3 SwayOffset()
        {
            float t = Time.time * 0.35f;
            return new Vector3((Mathf.PerlinNoise(t, 1.3f) - 0.5f) * sway * 2f, (Mathf.PerlinNoise(2.7f, t) - 0.5f) * sway * 2f, 0f);
        }
    }
}
