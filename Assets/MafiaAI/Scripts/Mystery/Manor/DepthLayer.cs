using UnityEngine;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>
    /// 원근 흉내: 직교 카메라로 찍지만, 깊이(z)가 있는 겹은 진짜 카메라처럼 덜(먼 겹)·더(가까운 겹) 움직이고,
    /// 카메라가 물러날수록(줌 아웃) 먼 겹은 덜 작아진다. REPLACED가 3D 무대에 도트를 세운 것과 같은 깊이감을 2D로 낸다.
    ///
    /// 자식들은 '그 깊이의 평면'에서 기준점(reference) 둘레에 배치한다. 매 프레임 카메라 거리 D = 직교 크기 × k로 보고,
    /// 투영 비율 p = D / (D + z)로 겹 전체를 옮기고 줄인다. z가 무한대면(하늘) 화면에 붙어 화면을 덮는다.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]   // 카메라(50)가 자리를 정한 뒤에 따라간다 — 한 프레임 늦으면 겹이 떨린다
    public class DepthLayer : MonoBehaviour
    {
        public Camera cam;
        public float depth = 40f;                       // 집 평면(0)에서 뒤로 얼마나(음수 = 카메라 쪽)
        public bool infinite;                           // 하늘처럼 아주 먼 겹 — 화면을 덮고 아주 조금만 흐른다
        public Vector2 reference = new Vector2(16f, 10f);
        public float focal = 3.3f;                      // 카메라 거리 = 직교 크기 × 이 값 (세로 화각 약 34°)
        public float infiniteDrift = 0.02f;             // 무한 겹이 카메라 이동에 따라 흐르는 정도
        public float infiniteScale = 1f;                // 무한 겹 크기(직교 크기 5.4 기준 배율)
        public bool pixelSnap = true;                   // 화면 1픽셀 격자에 맞춘다(도트가 헤엄치지 않게)

        void LateUpdate()
        {
            if (cam == null) return;
            var c = (Vector2)cam.transform.position;
            float size = cam.orthographic ? cam.orthographicSize : 5.4f;
            if (infinite)
            {
                float s = size / 5.4f * infiniteScale;
                var drift = (reference - c) * infiniteDrift;
                transform.position = Snap(new Vector3(c.x + drift.x, c.y + drift.y, transform.position.z), size);
                transform.localScale = new Vector3(s, s, 1f);
                return;
            }
            float d = size * focal;
            float p = d / Mathf.Max(0.5f, d + depth);
            var pos = c + (reference - c) * p;
            transform.position = Snap(new Vector3(pos.x, pos.y, transform.position.z), size);
            transform.localScale = new Vector3(p, p, 1f);
        }

        /// <summary>현재 줌에서 화면 1픽셀 단위로 자리를 맞춘다.</summary>
        Vector3 Snap(Vector3 p, float size)
        {
            if (!pixelSnap || cam.pixelHeight <= 0) return p;
            float unit = 2f * size / cam.pixelHeight;
            return new Vector3(Mathf.Round(p.x / unit) * unit, Mathf.Round(p.y / unit) * unit, p.z);
        }
    }
}
