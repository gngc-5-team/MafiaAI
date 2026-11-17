using UnityEngine;

namespace MafiaAI.Mystery.Manor
{
    /// <summary>천천히 흔들리는 것들(커튼·등불·덩굴): 피벗 둘레로 아주 작게 기운다.</summary>
    public class Sway : MonoBehaviour
    {
        public float degrees = 2f;
        public float speed = 0.7f;
        float _seed;
        Quaternion _base;

        void Awake()
        {
            _seed = Random.value * 10f;
            _base = transform.localRotation;
        }

        void Update()
        {
            float a = (Mathf.PerlinNoise(_seed, Time.time * speed) - 0.5f) * 2f * degrees;
            transform.localRotation = _base * Quaternion.Euler(0, 0, a);
        }
    }
}
