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
        public TMP_Text VoteCountText;

        /// <summary>이 카드에 마우스가 들어오거나 나갈 때(자기 자신, 들어옴 여부).</summary>
        public event Action<VoteCandidateView, bool> OnHoverChanged;

        const float LerpSpeed = 12f;
        float _targetScale = 1f;

        public void SetTargetScale(float scale) => _targetScale = scale;

        void Update()
        {
            if (Mathf.Approximately(transform.localScale.x, _targetScale)) return;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _targetScale, Time.deltaTime * LerpSpeed);
        }

        public void OnPointerEnter(PointerEventData eventData) => OnHoverChanged?.Invoke(this, true);
        public void OnPointerExit(PointerEventData eventData) => OnHoverChanged?.Invoke(this, false);
    }
}
