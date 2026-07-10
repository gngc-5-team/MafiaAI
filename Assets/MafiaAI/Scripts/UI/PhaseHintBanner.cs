using System.Collections;
using UnityEngine;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 페이즈 전환마다 조작 힌트를 몇 초 띄우는 배너 — GameController 이벤트에만 구독(다른 UI 무침범).
    /// 배너 오브젝트/텍스트는 씬에 저작하고, 여기서는 문구 갱신과 페이드만 한다.
    /// </summary>
    public class PhaseHintBanner : MonoBehaviour
    {
        [Header("게임 연결")]
        [SerializeField] GameController controller;

        [Header("연결(씬 저작)")]
        [SerializeField] CanvasGroup group;   // 배너 루트의 CanvasGroup(기본 alpha 0으로 저작)
        [SerializeField] TMP_Text label;

        [Header("표시")]
        [SerializeField] float showSeconds = 4.5f;
        [SerializeField] float fadeSeconds = 0.35f;
        [Tooltip("죽은 뒤(관전)에는 조작 힌트를 숨긴다")]
        [SerializeField] bool hideWhenDead = true;

        Coroutine _co;

        void Awake()
        {
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false; }
        }

        void OnEnable()
        {
            if (controller != null) controller.OnPhaseChanged += HandlePhase;
        }

        void OnDisable()
        {
            if (controller != null) controller.OnPhaseChanged -= HandlePhase;
        }

        void HandlePhase(GameState s)
        {
            if (group == null || label == null || s == null) return;

            var human = controller != null ? controller.HumanPlayer : null;
            if (human == null) return;                       // 전원 AI 관전이면 힌트 없음
            if (hideWhenDead && !human.Alive) return;        // 사망 후 관전

            string text = BuildHint(s.Phase, human.Role);
            if (string.IsNullOrEmpty(text)) return;

            label.text = text;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(FadeRoutine());
        }

        static string BuildHint(Phase phase, Role role)
        {
            switch (phase)
            {
                case Phase.Night:
                    switch (role)
                    {
                        case Role.Mafia:  return "밤 — 시야 밖은 어둠. 사냥감에게 다가가 [Space] 살해";
                        case Role.Police: return "밤 — 수상한 사람에게 다가가 [Space] 조사";
                        case Role.Doctor: return "밤 — 지킬 사람에게 다가가 [Space] 보호";
                        default:          return "밤 — 시야가 좁다. [WASD] 조심히 움직여 살아남아라";
                    }
                case Phase.Dawn:
                    return "아침 — 밤사이 일어난 일이 공개된다";
                case Phase.Discuss:
                    return "낮 토론 — [Enter] 발언 · [F] 단둘이 심문 · [M] 지도 · [Tab] 대화 기록\n같은 방에 있는 사람만 내 말을 듣는다";
                case Phase.Vote:
                    return "투표 — 처형할 사람을 선택하라";
                default:
                    return null; // End 등은 전용 UI가 담당
            }
        }

        IEnumerator FadeRoutine()
        {
            yield return Fade(group.alpha, 1f);
            yield return new WaitForSeconds(showSeconds);
            yield return Fade(group.alpha, 0f);
            _co = null;
        }

        IEnumerator Fade(float from, float to)
        {
            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.deltaTime;
                group.alpha = Mathf.Lerp(from, to, fadeSeconds <= 0f ? 1f : t / fadeSeconds);
                yield return null;
            }
            group.alpha = to;
        }
    }
}
