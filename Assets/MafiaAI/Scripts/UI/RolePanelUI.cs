using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 하이라키에 직접 배치한 "내 역할" 카드에 인간 플레이어의 역할/상태를 채운다.
    /// detailText는 선택 사항 — 안 만들었으면 비워둬도 동작한다(역할 이름만 표시).
    /// </summary>
    public class RolePanelUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] TMP_Text roleNameText;
        [SerializeField] TMP_Text detailText; // 현재 위치 / 조사 결과 / 사망 여부

        void Reset() => controller = GetComponent<GameController>();
        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (controller == null) controller = FindFirstObjectByType<GameController>();
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnGameSetup += Refresh;
            controller.OnPhaseChanged += HandlePhaseChanged;
            controller.OnLocationsChanged += Refresh;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnGameSetup -= Refresh;
            controller.OnPhaseChanged -= HandlePhaseChanged;
            controller.OnLocationsChanged -= Refresh;
        }

        void HandlePhaseChanged(GameState s) => Refresh();

        void Refresh()
        {
            var h = controller.HumanPlayer;
            if (h == null) return;

            if (roleNameText != null) roleNameText.text = h.Role.Korean();

            if (detailText != null)
            {
                string detail = "현재 위치: " + controller.GetPlayerRoom(h.Id);
                if (h.Role == Role.Police && h.Investigations.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var r in h.Investigations) parts.Add(r.TargetId + "=" + r.Result.Korean());
                    detail += "    [조사] " + string.Join(", ", parts);
                }
                if (!h.Alive) detail += "    (사망)";
                detailText.text = detail;
            }
        }
    }
}
