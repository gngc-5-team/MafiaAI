using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace MafiaAI.UI
{
    /// <summary>
    /// 엔딩 크레딧처럼 텍스트가 아래에서 위로 천천히 흐르는 연출.
    /// 크레딧 버튼 onClick에 Open()을, 닫기 버튼(또는 ESC/클릭)에 Close()를 연결한다.
    /// 내용은 이 스크립트 안에 있어서 따로 채워 넣을 필요 없다(수정도 여기서).
    /// </summary>
    public class CreditsRoll : MonoBehaviour
    {
        [SerializeField] GameObject panelRoot;     // 켜고 끌 크레딧 패널(비워두면 이 오브젝트 자신)
        [SerializeField] RectTransform scrollText; // 위로 흐를 TMP 텍스트의 RectTransform
        [SerializeField] TMP_Text creditsLabel;    // 그 텍스트 컴포넌트(내용을 코드가 채운다)
        [SerializeField] float scrollSpeed = 60f;  // 초당 올라가는 픽셀
        [SerializeField] float startOffset = -400f; // 시작 시 아래쪽 여백(화면 밑에서 올라오게)
        [SerializeField] bool loop = true;         // 끝까지 올라가면 처음부터 다시(끄면 끝에서 자동 닫힘)
        [SerializeField] bool closeWithEscape = true;

        const string CreditsBody =
            "<size=140%><b>STANDOFF</b></size>\n\n\n" +
            "<b>── 기획 / 개발 ──</b>\n강유민\n문종훈\n\n" +
            "<b>── 아트 / 협업 ──</b>\n우서혜\n이민희\n\n\n" +
            "<b>── 사용 기술 ──</b>\nUnity\nOllama · Gemma 3 12B (로컬 LLM)\n\n\n" +
            "<b>── 리소스 ──</b>\n" +
            "캐릭터 및 타이틀 : 우서혜\n"+
            "UI 및 타일 : 이민희\n"+
            "(디자인 Manus)\n" +
            "폰트 : 넥슨 워헤이븐체\n" +
            "아이콘 · 스프라이트 : 우서혜, 이민희\n\n\n" +
            "<b>── 팀 ──</b>\n우주미니문\n2026\n\n\n" +
            "<size=120%>플레이해주셔서 감사합니다!</size>\n";

        GameObject Target => panelRoot != null ? panelRoot : gameObject;
        float _resetY;   // 시작 위치(아래)
        float _endY;     // 다 올라간 위치(위)

        bool _opening; // 첫 Open()이 부른 활성화 도중 Awake가 도로 꺼버리는 걸 막는 가드

        void Awake()
        {
            if (creditsLabel != null) creditsLabel.text = CreditsBody;
            if (!_opening) Target.SetActive(false); // Open 도중 실행된 Awake라면 숨기지 않는다
        }

        void Update()
        {
            if (!Target.activeSelf) return;

            if (closeWithEscape)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            }

            if (scrollText == null) return;
            var pos = scrollText.anchoredPosition;
            pos.y += scrollSpeed * Time.unscaledDeltaTime;
            scrollText.anchoredPosition = pos;

            if (pos.y >= _endY)
            {
                if (loop) ResetScroll();
                else Close();
            }
        }

        public void Open()
        {
            if (creditsLabel != null) creditsLabel.text = CreditsBody; // Awake가 아직 안 돌았을 수 있으니 여기서도 채운다
            _opening = true;          // 아래 SetActive(true)로 Awake가 처음 돌아도 도로 꺼지지 않게
            Target.SetActive(true);
            ResetScroll();
        }

        public void Close() => Target.SetActive(false);

        void ResetScroll()
        {
            if (scrollText == null) return;
            // 텍스트가 자기 높이 + 화면 높이만큼 올라가면 완전히 지나간 것.
            float textHeight = scrollText.rect.height;
            float viewHeight = ((RectTransform)Target.transform).rect.height;
            _resetY = startOffset;
            _endY = textHeight + viewHeight;
            scrollText.anchoredPosition = new Vector2(scrollText.anchoredPosition.x, _resetY);
        }
    }
}
