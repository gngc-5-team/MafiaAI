using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 게임 부트스트랩: HumanActor를 만들어 GameController에 연결하고 게임을 시작한다.
    /// 화면에 보이는 UI는 전부 하이라키에 직접 배치한 오브젝트 + 전용 스크립트
    /// (TopBarUI, ChatLogUI, AvatarBarUI, RolePanelUI, InputBarUI, ChoiceOverlayUI)가 담당하며,
    /// 이 스크립트는 UI를 코드로 만들지 않는다.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public class MafiaUI : MonoBehaviour
    {
        [Tooltip("끄면 6명 전원 AI로 자동 진행(관전 모드). 켜면 한 좌석이 당신.")]
        public bool enableHumanSeat = true;

        GameController _controller;
        HumanActor _human;

        void Start()
        {
            EnsureEventSystem();

            _controller = GetComponent<GameController>();
            if (GetComponent<SpriteMansionView>() == null) gameObject.AddComponent<SpriteMansionView>();
            _controller.autoStart = false;
            _controller.includeHuman = enableHumanSeat;
            _controller.revealRolesAtStartForDebug = false;

            if (enableHumanSeat)
            {
                _human = new HumanActor();
                _controller.humanActor = _human;
            }

            _ = _controller.StartGameAsync();
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
