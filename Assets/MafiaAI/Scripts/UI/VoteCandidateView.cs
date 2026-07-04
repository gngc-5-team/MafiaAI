using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MafiaAI.UI
{
    /// <summary>
    /// 투표 후보 카드 프리팹에 붙이는 참조 홀더. ChoiceOverlayUI가 Instantiate해서 값만 채운다.
    /// Portrait는 캐릭터 초상화 이미지가 아직 없어도 비워둔 채로 동작한다 —
    /// 나중에 이미지가 생기면 이름→스프라이트 매핑만 추가해서 여기에 꽂으면 됨.
    /// 마우스 호버 시 커지는 연출(목표 크기로 부드럽게 보간)도 여기서 자체적으로 처리한다.
    /// </summary>
    public class VoteCandidateView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Button Button;
        public Image Background;
        public Image Portrait;
        public TMP_Text NameText;
        public TMP_Text VoteCountText;     // 숫자로 표 표시(선택). 이미지로 하려면 VoteTicks 사용
        public VoteTickDisplay VoteTicks;  // 이미지(체크)로 표 표시(선택). 안 넣으면 숫자만
        public GameObject DeadOverlay;     // 사망 표시(X 마크). 프리팹에 이미 있는 X UI를 연결하면 됨
        public GameObject SelectedOverlay; // 선택 표시(체크). 프리팹에 만든 체크 UI를 연결하면 됨

        /// <summary>이 카드에 마우스가 들어오거나 나갈 때(자기 자신, 들어옴 여부).</summary>
        public event Action<VoteCandidateView, bool> OnHoverChanged;

        /// <summary>호버로 인한 크기 변화를 받지 않는 상태(예: 죽은 카드). true면 항상 기본 크기.</summary>
        public bool HoverLocked { get; set; }

        const float LerpSpeed = 12f;
        float _targetScale = 1f;

        public void SetTargetScale(float scale) => _targetScale = scale;

        public void SetDead(bool dead)
        {
            HoverLocked = dead;
            if (DeadOverlay != null) DeadOverlay.SetActive(dead);
            if (dead) _targetScale = 1f;
        }

        void Update()
        {
            if (Mathf.Approximately(transform.localScale.x, _targetScale)) return;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _targetScale, Time.deltaTime * LerpSpeed);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (HoverLocked) return;
            OnHoverChanged?.Invoke(this, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (HoverLocked) return;
            OnHoverChanged?.Invoke(this, false);
        }
    }
}
