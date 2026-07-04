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

        readonly Dictionary<Light2D, float> _baseIntensities = new();
        readonly Dictionary<Light2D, float> _phaseOffsets = new();

        void Awake()
        {
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (globalLight == null)
            {
                var go = GameObject.Find("Global Light 2D");
                if (go != null) globalLight = go.GetComponent<Light2D>();
            }

            CacheFlickerLights();
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

            float target = controller.State.Phase == Phase.Night ? 0.19f : 0.44f;
            globalLight.intensity = Mathf.Lerp(globalLight.intensity, target, Time.deltaTime * 2.5f);
            globalLight.color = Color.Lerp(globalLight.color, controller.State.Phase == Phase.Night ? Hex("1B2740") : Hex("626A82"), Time.deltaTime * 1.8f);
        }

        void UpdateFlicker()
        {
            foreach (var light in flickerLights)
            {
                if (light == null || !_baseIntensities.TryGetValue(light, out var baseIntensity)) continue;
                float seed = _phaseOffsets.TryGetValue(light, out var offset) ? offset : 0f;
                float slow = Mathf.PerlinNoise(seed, Time.time * 0.9f);
                float fast = Mathf.PerlinNoise(seed + 17.3f, Time.time * 3.1f);
                float flicker = 0.88f + slow * 0.16f + fast * 0.06f;
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
