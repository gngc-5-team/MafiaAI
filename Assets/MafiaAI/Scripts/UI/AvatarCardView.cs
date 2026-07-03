using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.UI
{
    /// <summary>
    /// 생존자 카드 프리팹에 붙이는 참조 홀더. AvatarBarUI가 Instantiate해서 값만 채운다.
    /// 프리팹: 배경 Image 위에 이름(TMP)/상태(TMP) 텍스트 두 개를 자식으로 두고 이 컴포넌트에 연결.
    /// </summary>
    public class AvatarCardView : MonoBehaviour
    {
        public Image background;
        public TMP_Text nameText;
        public TMP_Text statusText;
    }
}
