using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MafiaAI.Mystery.UI
{
    /// <summary>마우스가 올라오고 나갈 때를 알린다(조사 지점·숨은 단서의 붉은 동그라미, 이동 비용 안내).</summary>
    public class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string key;
        public event Action<HoverRelay> Entered;
        public event Action<HoverRelay> Exited;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke(this);
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke(this);
    }
}
