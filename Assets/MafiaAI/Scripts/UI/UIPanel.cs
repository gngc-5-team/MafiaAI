using UnityEngine;
using UnityEngine.InputSystem;

namespace MafiaAI.UI
{
    /// <summary>
    /// 크레딧/설정처럼 버튼으로 열고 닫는 패널. 여는 버튼 onClick에 Open(), 닫기 버튼 onClick에 Close()를 연결한다.
    /// ESC로도 닫히고(옵션), 시작할 땐 꺼진 상태로 둔다.
    /// </summary>
    public class UIPanel : MonoBehaviour
    {
        [SerializeField] GameObject panelRoot;      // 켜고 끌 패널(비워두면 이 오브젝트 자신)
        [SerializeField] bool closeWithEscape = true;
        [SerializeField] bool startHidden = true;

        GameObject Target => panelRoot != null ? panelRoot : gameObject;

        void Awake()
        {
            if (startHidden) Target.SetActive(false);
        }

        void Update()
        {
            if (!closeWithEscape || !Target.activeSelf) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        public void Open() => Target.SetActive(true);
        public void Close() => Target.SetActive(false);
        public void Toggle() => Target.SetActive(!Target.activeSelf);
    }
}
