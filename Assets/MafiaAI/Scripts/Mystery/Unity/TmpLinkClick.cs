using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MafiaAI.Mystery.UI
{
    /// <summary>TMP 글 속 &lt;link="id"&gt; 부분을 눌렀을 때 알린다(재구성 문장의 빈칸).</summary>
    [RequireComponent(typeof(TMP_Text))]
    public class TmpLinkClick : MonoBehaviour, IPointerClickHandler
    {
        public event Action<string> LinkClicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            var text = GetComponent<TMP_Text>();
            int index = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, eventData.pressEventCamera);
            if (index < 0) return;
            LinkClicked?.Invoke(text.textInfo.linkInfo[index].GetLinkID());
        }
    }
}
