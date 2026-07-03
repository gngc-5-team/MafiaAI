using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 밤에 마피아/의사/경찰이면 클릭 리스트 대신 직접 걸어가서 대상 근처에서 K를 눌러 확정한다.
    /// 다른 플레이어(AI)는 낮 마지막 위치에 멈춰 서 있는 것처럼 보인다 — SpriteMansionView가
    /// 밤에는 AI 위치를 갱신하지 않으므로 따로 처리하지 않아도 이미 그렇게 보인다.
    /// 밤이 끝나(새벽) 인간 플레이어 위치는 밤 시작 직전(낮 토론 마지막) 위치로 되돌린다.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public class NightMovementUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] SpriteMansionView mansionView;
        [SerializeField] TMP_Text hintText; // 선택: "K를 눌러 OOO 지목" 안내. 안 만들어도 동작함

        const float SelectRadius = 1.6f;

        HumanActor _human;
        List<string> _candidates;

        Vector3 _preNightPos;
        string _preNightRoom;
        bool _hasPreNightSnapshot;

        void Reset()
        {
            controller = GetComponent<GameController>();
            mansionView = GetComponent<SpriteMansionView>();
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (mansionView == null) mansionView = GetComponent<SpriteMansionView>();
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnGameSetup += TryBindHuman;
            controller.OnPhaseChanged += HandlePhaseChanged;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnGameSetup -= TryBindHuman;
            controller.OnPhaseChanged -= HandlePhaseChanged;
            Unbind();
        }

        void TryBindHuman()
        {
            Unbind();
            _human = controller.humanActor as HumanActor;
            if (_human == null) return; // 관전 모드(인간 좌석 없음)
            _human.OnNeedChoice += HandleNeedChoice;
            _human.OnChoiceResolved += HandleResolved;
        }

        void Unbind()
        {
            if (_human == null) return;
            _human.OnNeedChoice -= HandleNeedChoice;
            _human.OnChoiceResolved -= HandleResolved;
            _human = null;
        }

        void HandlePhaseChanged(GameState s)
        {
            if (controller.HumanPlayer == null || mansionView == null) return;

            if (s.Phase == Phase.Night)
            {
                _preNightPos = mansionView.HumanWorldPosition;
                _preNightRoom = controller.GetPlayerRoom(controller.HumanPlayer.Id);
                _hasPreNightSnapshot = true;
            }
            else if (s.Phase == Phase.Dawn && _hasPreNightSnapshot)
            {
                mansionView.SetHumanPosition(_preNightPos, _preNightRoom);
                controller.MoveHumanToRoom(_preNightRoom);
                _hasPreNightSnapshot = false;
            }
        }

        void HandleNeedChoice(Player self, List<string> candidates, string kind)
        {
            // 투표는 ChoiceOverlayUI가 클릭 리스트로 처리한다. 여기는 밤 능력만.
            _candidates = kind == "vote" ? null : candidates;
            SetHint();
        }

        void HandleResolved()
        {
            _candidates = null;
            SetHint();
        }

        void Update()
        {
            if (_candidates == null || _human == null || mansionView == null) return;
            SetHint();

            var kb = Keyboard.current;
            if (kb == null || !kb.kKey.wasPressedThisFrame) return;

            string nearest = FindNearestCandidate();
            if (nearest != null) _human.SubmitChoice(nearest);
        }

        string FindNearestCandidate()
        {
            Vector3 me = mansionView.HumanWorldPosition;
            string best = null;
            float bestDist = SelectRadius;
            foreach (var id in _candidates)
            {
                if (!mansionView.TryGetTokenPosition(id, out var pos)) continue;
                float d = Vector3.Distance(me, pos);
                if (d < bestDist) { bestDist = d; best = id; }
            }
            return best;
        }

        void SetHint()
        {
            if (hintText == null) return;
            if (_candidates == null) { hintText.text = ""; return; }
            string near = FindNearestCandidate();
            hintText.text = near != null ? ("K를 눌러 " + near + " 지목") : "대상 가까이 가서 K를 누르세요";
        }
    }
}
