using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

namespace MafiaAI.UI
{
    /// <summary>
    /// 시작 전 튜토리얼 패널 — 씬에 저작된 페이지(인게임 스크린샷+설명)를 넘겨 본다.
    /// StartGame이 Open(onFinished)으로 열고, 마지막 장의 '시작'/건너뛰기/Esc가 onFinished를 호출해 게임을 시작한다.
    /// 페이지 내용은 씬의 스프라이트/TMP가 원본(런타임 생성 없음), 여기서는 활성 전환만 한다.
    /// </summary>
    public class HowToPlayUI : MonoBehaviour
    {
        [Header("연결(씬 저작)")]
        [SerializeField] GameObject panel;       // 패널 루트(기본 비활성)
        [SerializeField] GameObject[] pages;     // 페이지 오브젝트들(순서 = 표시 순서)
        [SerializeField] Button prevButton;
        [SerializeField] Button nextButton;      // 마지막 장에서는 '시작' 역할
        [SerializeField] Button skipButton;      // 건너뛰고 바로 시작
        [SerializeField] TMP_Text nextLabel;     // 다음 버튼 라벨(마지막 장에서 '시작'으로 교체)
        [SerializeField] TMP_Text pageLabel;     // "1 / 5"

        int _page;
        System.Action _onFinished;

        void Awake()
        {
            if (prevButton != null) prevButton.onClick.AddListener(() => Show(_page - 1));
            if (nextButton != null) nextButton.onClick.AddListener(Next);
            if (skipButton != null) skipButton.onClick.AddListener(Finish);
            if (panel != null) panel.SetActive(false);
        }

        void Update()
        {
            if (panel == null || !panel.activeSelf) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame) Finish();
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Next();
            else if (kb.leftArrowKey.wasPressedThisFrame) Show(_page - 1);
        }

        /// <summary>튜토리얼을 열고, 끝나면(마지막 장 통과/건너뛰기/Esc) onFinished를 1회 호출한다.</summary>
        public void Open(System.Action onFinished)
        {
            _onFinished = onFinished;
            if (panel == null) { Finish(); return; }
            panel.SetActive(true);
            Show(0);
        }

        void Next()
        {
            if (_page >= pages.Length - 1) Finish();
            else Show(_page + 1);
        }

        void Finish()
        {
            if (panel != null) panel.SetActive(false);
            var cb = _onFinished;
            _onFinished = null;
            if (cb != null) cb();
        }

        void Show(int page)
        {
            if (pages == null || pages.Length == 0) return;
            _page = Mathf.Clamp(page, 0, pages.Length - 1);
            for (int i = 0; i < pages.Length; i++)
                if (pages[i] != null) pages[i].SetActive(i == _page);
            if (pageLabel != null) pageLabel.text = (_page + 1) + " / " + pages.Length;
            if (prevButton != null) prevButton.interactable = _page > 0;
            if (nextLabel != null) nextLabel.text = _page >= pages.Length - 1 ? "시 작" : "▶";
        }
    }
}
