using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 게임 종료 시 엔딩 일러스트 + 다시 시작 버튼.
    /// UI는 씬의 EndingCanvas에 저작되어 있고 이 스크립트는 참조+로직만.
    /// 플레이어가 이겼을 때만 일러스트: 성별(여=하루/노아/세이, 남=카이/미로/제로) × 진영(시민/마피아) 4종.
    /// 졌을 때(또는 무승부)는 일러스트 없이 결과 문구 + 다시 시작.
    /// 다시 시작 = 씬 리로드(동봉 AI 서버는 static으로 유지되므로 재기동 없이 즉시 새 판).
    /// </summary>
    public class EndingUI : MonoBehaviour
    {
        [Header("게임 연결")]
        [SerializeField] GameController controller;

        [Header("씬 저작 UI 참조")]
        [SerializeField] GameObject root;          // EndingCanvas/Root (기본 비활성)
        [SerializeField] Image artImage;           // 엔딩 일러스트(캔버스 꽉 채움)
        [SerializeField] Button restartButton;     // 일러스트 위에 얹힘

        [Header("엔딩 일러스트 (승리 진영별)")]
        [SerializeField] Sprite citizenWin;        // citizen_win — 시민 진영 승리
        [SerializeField] Sprite mafiaWin;          // mafia_Win — 마피아 진영 승리

        void Awake()
        {
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (root != null) root.SetActive(false);
        }

        void OnEnable()
        {
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (controller != null) controller.OnGameEnd += HandleEnd;
        }

        void OnDisable()
        {
            if (controller != null) controller.OnGameEnd -= HandleEnd;
        }

        void HandleEnd(Winner w)
        {
            if (root == null) return;
            root.SetActive(true);

            // 일러스트만으로 결과를 전달한다(이긴 진영 기준). 별도 문구 없음 — 아트에 이미 다 있다.
            Sprite art = w == Winner.Mafia ? mafiaWin : (w == Winner.Citizens ? citizenWin : null);
            if (artImage != null) { artImage.sprite = art; artImage.enabled = art != null; }
        }

        void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
