using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.UI
{
    /// <summary>
    /// 투표 후보 카드 프리팹에 붙이는 참조 홀더. ChoiceOverlayUI가 Instantiate해서 값만 채운다.
    /// Portrait는 캐릭터 초상화 이미지가 아직 없어도 비워둔 채로 동작한다 —
    /// 나중에 이미지가 생기면 이름→스프라이트 매핑만 추가해서 여기에 꽂으면 됨.
    /// </summary>
    public class VoteCandidateView : MonoBehaviour
    {
        public Button Button;
        public Image Background;
        public Image Portrait;
        public TMP_Text NameText;
        public TMP_Text VoteCountText;
    }
}
