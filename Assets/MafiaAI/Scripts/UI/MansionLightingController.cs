using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// Keeps the mansion lighting alive without generating the map itself.
    /// Scene lights are placed under a Lighting root in edit mode; this only follows the player
    /// light and adds subtle candle-style flicker at runtime.
    /// </summary>
    public class MansionLightingController : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] SpriteMansionView mansionView;
        [SerializeField] Light2D globalLight;
        [SerializeField] List<Light2D> flickerLights = new();

        [Header("전역 무드 (Global Light 낮/밤)")]
        [SerializeField] float dayGlobalIntensity = 0.44f;
        [SerializeField] float nightGlobalIntensity = 0.19f;
        [SerializeField] Color dayGlobalColor = new Color(0.384f, 0.416f, 0.510f);   // #626A82
        [SerializeField] Color nightGlobalColor = new Color(0.106f, 0.153f, 0.251f); // #1B2740
        [SerializeField] float globalIntensityLerpSpeed = 2.5f;
        [SerializeField] float globalColorLerpSpeed = 1.8f;

        [Header("창문 달빛 (오토타일러 WindowAnchors에 스냅되는 씬 라이트 풀)")]
        [SerializeField] Transform moonlightPool;               // Lighting/Window Moonlight (자식 = 예비 달빛들)
        [SerializeField] float moonlightDayIntensity = 0.3f;    // 낮: 은은한 채광
        [SerializeField] float moonlightNightIntensity = 1.05f; // 밤: 차가운 달빛이 주광
        [SerializeField] float moonlightIntensityLerpSpeed = 2.5f;
        [SerializeField] Color moonlightColor = new Color(0.56f, 0.66f, 0.85f); // 청백
        [SerializeField] float moonlightInnerRadius = 0.4f;
        [SerializeField] float moonlightOuterRadius = 7.5f;
        [SerializeField] float moonlightBaseOuterAngle = 55f;   // 창 폭 1칸 기준 원뿔 바깥각
        [SerializeField] float moonlightAnglePerExtraWidth = 18f; // 창 폭 1칸 늘 때마다 원뿔 +°
        [Range(0f, 1f)][SerializeField] float moonlightInnerAngleRatio = 0.45f; // 안쪽각 = 바깥각 × 비율(소프트 가장자리)
        [SerializeField] float moonlightDropOffset = 0.6f;      // 창 아래로 내려 붙이는 거리(방 안쪽)
        [SerializeField] float moonlightRotationZ = 180f;       // 원뿔 방향(=transform.up). 180 = 아래로

        [Header("촛불 플리커 (flickerLights 목록에 적용)")]
        [SerializeField] float flickerBase = 0.88f;      // 기준 배율(1=플리커 없음에 가깝게)
        [SerializeField] float flickerSlowAmplitude = 0.16f;
        [SerializeField] float flickerSlowSpeed = 0.9f;
        [SerializeField] float flickerFastAmplitude = 0.06f;
        [SerializeField] float flickerFastSpeed = 3.1f;

        readonly List<Light2D> _activeMoonlights = new();

        readonly Dictionary<Light2D, float> _baseIntensities = new();
        readonly Dictionary<Light2D, float> _phaseOffsets = new();

        void Awake()
        {
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (mansionView == null) mansionView = FindFirstObjectByType<SpriteMansionView>();
            if (globalLight == null)
            {
                var go = GameObject.Find("Global Light 2D");
                if (go != null) globalLight = go.GetComponent<Light2D>();
            }
            if (moonlightPool == null)
            {
                var go = GameObject.Find("Window Moonlight");
                if (go != null) moonlightPool = go.transform;
            }

            CacheFlickerLights();
            controller.OnGameSetup += HandleGameSetup;
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnGameSetup -= HandleGameSetup;
        }

        // LayoutMansion(SpriteMansionView의 OnGameSetup 핸들러)이 방을 옮긴 뒤에 스냅해야 하므로 한 프레임 늦춘다.
        void HandleGameSetup() => StartCoroutine(SnapNextFrame());

        IEnumerator SnapNextFrame()
        {
            yield return null;
            SnapLightsToLayout();
        }

        /// <summary>
        /// 씬에 미리 배치된 창문 달빛을 이번 판 레이아웃 위치로 옮긴다(런타임 생성 없음).
        /// (방 앰버 램프·복도 파란 조명은 2026-07-04 사용자 요청으로 제거 — 조명은 창문 달빛이 담당.)
        /// </summary>
        void SnapLightsToLayout()
        {
            SnapMoonlights();
            CacheFlickerLights(); // 바뀐 intensity를 플리커 기준값으로 재캐시
        }

        /// <summary>
        /// 오토타일러가 이번 판에 배치한 창문(WindowAnchors)마다 씬 풀의 달빛을 하나씩 스냅한다.
        /// 창 폭에 따라 원뿔을 넓히고, 남는 풀 라이트는 꺼둔다. 창이 풀보다 많으면 초과분은 생략.
        /// </summary>
        void SnapMoonlights()
        {
            _activeMoonlights.Clear();
            if (moonlightPool == null || mansionView == null) return;

            var anchors = mansionView.WindowAnchors;
            int used = 0;
            foreach (Transform child in moonlightPool)
            {
                var l = child.GetComponent<Light2D>();
                if (l == null) continue;

                if (used < anchors.Count)
                {
                    var a = anchors[used++];
                    // 창 아래줄 바로 밑(방 안쪽)에서 아래로 떨어지는 좁은 셰프트
                    child.position = new Vector3(a.Pos.x, a.Pos.y - moonlightDropOffset, child.position.z);
                    child.rotation = Quaternion.Euler(0f, 0f, moonlightRotationZ); // 원뿔(=transform.up) 방향
                    l.color = moonlightColor;
                    l.pointLightInnerRadius = moonlightInnerRadius;
                    l.pointLightOuterRadius = moonlightOuterRadius;
                    float outer = moonlightBaseOuterAngle + (a.Width - 1) * moonlightAnglePerExtraWidth;
                    l.pointLightInnerAngle = outer * moonlightInnerAngleRatio;
                    l.pointLightOuterAngle = outer;
                    l.intensity = moonlightDayIntensity;
                    child.gameObject.SetActive(true);
                    _activeMoonlights.Add(l);
                }
                else child.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            UpdateGlobalMood();
            UpdateFlicker();
        }

        void CacheFlickerLights()
        {
            _baseIntensities.Clear();
            _phaseOffsets.Clear();

            foreach (var light in flickerLights)
            {
                if (light == null) continue;
                _baseIntensities[light] = light.intensity;
                _phaseOffsets[light] = Random.Range(0f, 10f);
            }
        }

        void UpdateGlobalMood()
        {
            if (globalLight == null || controller == null || controller.State == null) return;

            bool night = controller.State.Phase == Phase.Night;
            float target = night ? nightGlobalIntensity : dayGlobalIntensity;
            globalLight.intensity = Mathf.Lerp(globalLight.intensity, target, Time.deltaTime * globalIntensityLerpSpeed);
            globalLight.color = Color.Lerp(globalLight.color, night ? nightGlobalColor : dayGlobalColor, Time.deltaTime * globalColorLerpSpeed);

            // 달빛은 밤에 주광으로 올라오고 낮엔 은은한 채광으로 내려간다.
            float moonTarget = night ? moonlightNightIntensity : moonlightDayIntensity;
            foreach (var m in _activeMoonlights)
                if (m != null) m.intensity = Mathf.Lerp(m.intensity, moonTarget, Time.deltaTime * moonlightIntensityLerpSpeed);
        }

        void UpdateFlicker()
        {
            foreach (var light in flickerLights)
            {
                if (light == null || !_baseIntensities.TryGetValue(light, out var baseIntensity)) continue;
                float seed = _phaseOffsets.TryGetValue(light, out var offset) ? offset : 0f;
                float slow = Mathf.PerlinNoise(seed, Time.time * flickerSlowSpeed);
                float fast = Mathf.PerlinNoise(seed + 17.3f, Time.time * flickerFastSpeed);
                float flicker = flickerBase + slow * flickerSlowAmplitude + fast * flickerFastAmplitude;
                light.intensity = baseIntensity * flicker;
            }
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            return color;
        }
    }
}
