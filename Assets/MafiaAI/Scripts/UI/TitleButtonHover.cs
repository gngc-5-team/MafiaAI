using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace MafiaAI.UI
{
    /// <summary>
    /// 타이틀 화면 버튼용 호버 연출. 마우스를 올리면:
    ///  - 버튼이 하얘지고(라벨/배경 색)
    ///  - 살짝 커지며 오른쪽(안쪽)으로 밀려 나오고
    ///  - 왼쪽에 "가리키는 이미지"(pointer)가 나타난다.
    /// 각 타이틀 버튼(시작/설정/크레딧/종료)에 하나씩 붙이면 된다. 클릭 동작은 기존 Button.onClick 그대로.
    /// </summary>
    public class TitleButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [Header("색이 바뀔 대상 (없으면 비워둬도 됨)")]
        [SerializeField] Graphic[] tintTargets;   // 라벨 텍스트/배경 Image 등 하얘질 것들
        [SerializeField] Color normalColor = new Color(1f, 1f, 1f, 0.75f);
        [SerializeField] Color hoverColor = Color.white;

        [Header("움직임")]
        [SerializeField] float hoverScale = 1.67f;   // 커지는 정도
        [SerializeField] float slideX = 18f;         // 오른쪽으로 밀려나는 거리(px)
        [SerializeField] float lerpSpeed = 14f;      // 부드럽게 따라가는 속도

        [Header("왼쪽 가리키는 이미지")]
        [SerializeField] GameObject pointer;         // 마우스 올렸을 때만 켜질 화살표/손 이미지

        [Header("밑줄 (없으면 비워둬도 됨)")]
        [SerializeField] GameObject underline;       // 마우스 올렸을 때만 켜질 밑줄 이미지

        RectTransform _rt;
        Vector2 _baseAnchoredPos;
        bool _hovered;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _baseAnchoredPos = _rt.anchoredPosition;
            SetHover(false, instant: true);
        }

        void OnDisable()
        {
            // 비활성화될 때(씬 전환 등) 상태를 원위치로 — 다음에 켜질 때 튀지 않게.
            _hovered = false;
            if (pointer != null) pointer.SetActive(false);
            if (underline != null) underline.SetActive(false);
        }

        void Update()
        {
            Vector2 targetPos = _baseAnchoredPos + (_hovered ? new Vector2(slideX, 0f) : Vector2.zero);
            float targetScale = _hovered ? hoverScale : 1.5f;

            _rt.anchoredPosition = Vector2.Lerp(_rt.anchoredPosition, targetPos, Time.unscaledDeltaTime * lerpSpeed);
            _rt.localScale = Vector3.Lerp(_rt.localScale, Vector3.one * targetScale, Time.unscaledDeltaTime * lerpSpeed);
        }

        public void OnPointerEnter(PointerEventData e) => SetHover(true);
        public void OnPointerExit(PointerEventData e) => SetHover(false);
        public void OnSelect(BaseEventData e) => SetHover(true);      // 키보드/패드 네비게이션도 지원
        public void OnDeselect(BaseEventData e) => SetHover(false);

        void SetHover(bool on, bool instant = false)
        {
            _hovered = on;

            if (tintTargets != null)
                foreach (var g in tintTargets)
                    if (g != null) g.color = on ? hoverColor : normalColor;

            if (pointer != null) pointer.SetActive(on);
            if (underline != null) underline.SetActive(on);

            if (instant)
            {
                _rt.anchoredPosition = _baseAnchoredPos;
                _rt.localScale = Vector3.one;
            }
        }
    }
}
