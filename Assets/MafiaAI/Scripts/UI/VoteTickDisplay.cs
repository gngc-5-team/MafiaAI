using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MafiaAI.UI
{
    /// <summary>
    /// 득표 수를 숫자 대신 체크(틱) 이미지 개수로 보여준다. SetCount(n)을 부르면 체크 이미지를 n개 켠다.
    /// tickRoot(Horizontal Layout Group 붙은 빈 컨테이너)에 tickPrefab(체크 Image 하나)을 필요한 만큼 복제.
    /// 크기는 tickPrefab/컨테이너 설정으로 정하므로, 큰 체크용/작은 체크용 프리팹을 따로 만들어 각각 연결하면 됨.
    /// </summary>
    public class VoteTickDisplay : MonoBehaviour
    {
        [SerializeField] Transform tickRoot;   // 체크들이 나열될 컨테이너(Horizontal Layout Group 권장)
        [SerializeField] Image tickPrefab;     // 체크 이미지 하나(프리팹 또는 씬 자식)
        [SerializeField] int maxTicks = 12;    // 안전 상한(표가 이보다 많아도 이 개수까지만 그림)

        readonly List<Image> _ticks = new();

        public void SetCount(int count)
        {
            if (tickRoot == null || tickPrefab == null) return;
            count = Mathf.Clamp(count, 0, maxTicks);

            while (_ticks.Count < count)
            {
                var t = Instantiate(tickPrefab, tickRoot);
                t.gameObject.SetActive(true);
                _ticks.Add(t);
            }
            for (int i = 0; i < _ticks.Count; i++)
                _ticks[i].gameObject.SetActive(i < count);
        }
    }
}
