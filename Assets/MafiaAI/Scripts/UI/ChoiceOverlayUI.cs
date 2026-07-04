using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 밤 능력(마피아/경찰/의사)과 투표에서 대상을 고르는 오버레이.
    /// UI는 하이라키에 직접 배치해두고 이 스크립트는 참조만 한다(런타임에 UI를 코드로 만들지 않음).
    /// HumanActor.OnNeedChoice가 오면 후보만큼 candidateButtonPrefab을 buttonListRoot 밑에 찍는다.
    /// </summary>
    public class ChoiceOverlayUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] GameObject overlayRoot;
        [SerializeField] TMP_Text titleText;
        [SerializeField] Transform buttonListRoot;
        [SerializeField] Button candidateButtonPrefab;

        HumanActor _human;

        void Reset()
        {
            controller = GetComponent<GameController>();
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        void OnEnable()
        {
            if (controller != null) controller.OnGameSetup += TryBindHuman;
        }

        void OnDisable()
        {
            if (controller != null) controller.OnGameSetup -= TryBindHuman;
            Unbind();
        }

        // GameController.Setup()에서 HumanActor가 만들어진 직후(OnGameSetup) 연결한다.
        void TryBindHuman()
        {
            Unbind();
            _human = controller.humanActor as HumanActor;
            if (_human == null) return; // 관전 모드(인간 좌석 없음)
            _human.OnNeedChoice += HandleNeedChoice;
            _human.OnChoiceResolved += HideOverlay;
        }

        void Unbind()
        {
            if (_human == null) return;
            _human.OnNeedChoice -= HandleNeedChoice;
            _human.OnChoiceResolved -= HideOverlay;
            _human = null;
        }

        void HandleNeedChoice(Player self, List<string> candidates, string kind)
        {
            // 밤 능력(마피아/의사/경찰) 대상 선택은 NightMovementUI가 담당한다(걸어가서 K로 확정).
            // 이 오버레이는 투표에만 쓴다.
            if (kind != "vote") return;

            // 하이라키/프리팹을 아직 안 만들었으면 조용히 아무 것도 안 하고 넘어간다(사망 시간 초과로 자동 처리됨).
            if (overlayRoot == null || buttonListRoot == null || candidateButtonPrefab == null) return;

            if (titleText != null) titleText.text = TitleFor(kind);

            foreach (Transform child in buttonListRoot) Destroy(child.gameObject);
            foreach (var id in candidates)
            {
                string target = id;
                var btn = Instantiate(candidateButtonPrefab, buttonListRoot);
                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = id;
                btn.onClick.AddListener(() => _human.SubmitChoice(target));
            }

            overlayRoot.SetActive(true);
        }

        void HideOverlay()
        {
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        static string TitleFor(string kind)
        {
            switch (kind)
            {
                case "vote": return "누구를 처형에 투표하시겠습니까?";
                case "mafia": return "[밤] 제거할 대상을 고르세요";
                case "police": return "[밤] 조사할 대상을 고르세요";
                case "doctor": return "[밤] 보호할 대상을 고르세요";
                default: return "대상을 고르세요";
            }
        }
    }
}
