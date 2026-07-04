using System;
using System.Collections.Generic;
using UnityEngine;

namespace MafiaAI.UI
{
    /// <summary>
    /// 페르소나 이름(카이/린/제로/미로/라온/하루/노아/세이) → 초상화 스프라이트 매핑.
    /// 카드 프리팹 하나엔 초상화 슬롯이 하나뿐이고, 실행 중에 이 표를 찾아서 그 이름에 맞는
    /// 그림 한 장을 그 슬롯에 꽂아준다. 이미지가 아직 없으면 비워둬도 동작(그냥 초상화 없이 표시).
    /// 투표 화면뿐 아니라 나중에 1:1 대화창에서도 그대로 재사용 가능.
    /// </summary>
    public class PersonaPortraitLibrary : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string Name;
            public Sprite Portrait;
        }

        [SerializeField] List<Entry> portraits = new();

        public Sprite GetPortrait(string personaName)
        {
            foreach (var e in portraits)
                if (e.Name == personaName) return e.Portrait;
            return null;
        }
    }
}
