using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 밤 공간 능력(마피아 살해 / 경찰 조사 / 의사 보호) 통합 시스템.
    /// 세 역할 모두 직접 걸어가서 대상에게 KillRadius 안으로 접근한 뒤 Space로 능력을 쓴다.
    /// - 사거리 안 최근접 대상 머리 위에 PointArrow(애니메이션)를 띄우고, 멀어지면 숨긴다.
    /// - 경찰: 사용 즉시 결과를 알 수 있다(즉시 기록 + 화면 표식).
    /// - 마피아/의사: 효과는 새벽 정산 때 적용(기존 파이프라인 그대로).
    /// - 의사가 밤 동안 아무에게도 안 쓰면 자힐(GameController.DefaultNightTarget이 처리).
    /// 밤이 끝나면 인간 위치는 밤 시작 직전 위치로 되돌린다.
    /// </summary>
    public class NightMovementUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] SpriteMansionView mansionView;
        [SerializeField] TMP_Text hintText; // 선택: "Space를 눌러 OOO 살해" 안내. 안 만들어도 동작함

        [Header("사거리 표식 (PointArrow, 대상 머리 위)")]
        [SerializeField] Sprite[] arrowFrames;          // PointArrow.aseprite Frame_0~2
        [SerializeField] float arrowFps = 4f;           // 3프레임 / 0.75s 클립 기준
        [SerializeField] float arrowScale = 7f;         // 9x10px@100PPU가 작아서 확대
        [SerializeField] float arrowHeadOffset = 1.15f; // 대상 머리 위 높이(이름표 아래)
        [SerializeField] float arrowBobAmplitude = 0.12f;
        [SerializeField] float arrowBobSpeed = 3.5f;
        [SerializeField] int arrowSortingOrder = 520;   // 밤 암전(500) 위

        [Tooltip("켜면 새벽에 플레이어를 밤 시작 위치로 되돌린다. 테스터 피드백('강제 워프가 어색하다')으로 기본 꺼짐 — 밤에 이동한 자리에서 아침을 맞는다.")]
        [SerializeField] bool returnToPreNightPosition = false;

        HumanActor _human;
        List<string> _candidates;

        Vector3 _preNightPos;
        string _preNightRoom;
        bool _hasPreNightSnapshot;

        GameObject _arrow;
        SpriteRenderer _arrowSr;
        float _arrowClock;

        void Reset()
        {
            controller = GetComponent<GameController>();
            mansionView = GetComponent<SpriteMansionView>();
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (mansionView == null) mansionView = GetComponent<SpriteMansionView>();
            if (mansionView == null) mansionView = FindFirstObjectByType<SpriteMansionView>();
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
            _human.OnNeedSpatialKill += HandleNeedSpatialKill;
            _human.OnChoiceResolved += HandleResolved;
        }

        void Unbind()
        {
            if (_human == null) return;
            _human.OnNeedChoice -= HandleNeedChoice;
            _human.OnNeedSpatialKill -= HandleNeedSpatialKill;
            _human.OnChoiceResolved -= HandleResolved;
            _human = null;
        }

        void HandlePhaseChanged(GameState s)
        {
            if (controller.HumanPlayer == null || mansionView == null) return;

            if (s.Phase == Phase.Night)
            {
                if (!_hasPreNightSnapshot)
                {
                    _preNightPos = mansionView.HumanWorldPosition;
                    _preNightRoom = controller.GetPlayerRoom(controller.HumanPlayer.Id);
                    _hasPreNightSnapshot = true;
                }
            }
            else if (s.Phase == Phase.Dawn && _hasPreNightSnapshot)
            {
                if (returnToPreNightPosition)
                {
                    mansionView.SetHumanPosition(_preNightPos, _preNightRoom);
                    controller.MoveHumanToRoom(_preNightRoom);
                }
                else
                {
                    // 밤에 이동한 현재 위치를 그대로 인정 — 방 소속만 실제 위치와 동기화한다.
                    string room = controller.GetPlayerRoom(controller.HumanPlayer.Id);
                    if (!string.IsNullOrEmpty(room)) controller.MoveHumanToRoom(room);
                }
                _hasPreNightSnapshot = false;
            }

            if (s.Phase != Phase.Night) HideArrow();
        }

        void HandleNeedChoice(Player self, List<string> candidates, string kind)
        {
            // 투표는 ChoiceOverlayUI가 클릭 리스트로 처리한다. 여기는 밤 능력만.
            _candidates = kind == "vote" ? null : candidates;
            SetHint();
        }

        // 인간 마피아 공간 사냥(HumanActor.SpatialKillMode 경로)도 같은 흐름으로 통합.
        void HandleNeedSpatialKill(Player self, List<string> candidates)
        {
            _candidates = candidates;
            SetHint();
        }

        void HandleResolved()
        {
            _candidates = null;
            SetHint();
            HideArrow();
        }

        void Update()
        {
            if (_candidates == null || _human == null || mansionView == null)
            {
                HideArrow();
                return;
            }

            string nearest = FindNearestCandidate();
            UpdateArrow(nearest);
            SetHint();

            if (nearest == null || IsTyping()) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.spaceKey.wasPressedThisFrame) return;

            UseAbility(nearest);
        }

        void UseAbility(string targetId)
        {
            var role = controller.HumanPlayer != null ? controller.HumanPlayer.Role : Role.Citizen;

            // 경찰: 사용 '즉시' 결과 기록 + 표식(다른 역할은 새벽 정산).
            if (role == Role.Police)
            {
                var faction = controller.SubmitImmediateInvestigation(targetId);
                if (faction.HasValue)
                {
                    bool mafia = faction.Value == Faction.Mafia;
                    ShowFloatingText(targetId, mafia ? "마피아!" : "시민", mafia ? Hex("E0503A") : Hex("6FA8DC"));
                }
            }
            else if (role == Role.Mafia)
            {
                mansionView.PlayKillFeedback(targetId); // 붉은 틴트 + "제거" (사망 공개는 새벽에)
            }
            else if (role == Role.Doctor)
            {
                ShowFloatingText(targetId, "보호", Hex("7BC96F"));
            }

            _human.SubmitChoice(targetId); // 확정 → OnChoiceResolved로 후보/표식 정리
        }

        string FindNearestCandidate()
        {
            Vector3 me = mansionView.HumanWorldPosition;
            float radius = controller != null && controller.config != null ? controller.config.KillRadius : 1.6f;
            string best = null;
            float bestDist = radius;
            foreach (var id in _candidates)
            {
                if (!mansionView.TryGetTokenPosition(id, out var pos)) continue;
                float d = Vector3.Distance(me, pos);
                if (d < bestDist) { bestDist = d; best = id; }
            }
            return best;
        }

        // ---------- PointArrow 표식 ----------

        void UpdateArrow(string targetId)
        {
            if (targetId == null || arrowFrames == null || arrowFrames.Length == 0)
            {
                HideArrow();
                return;
            }
            if (!mansionView.TryGetTokenPosition(targetId, out var pos))
            {
                HideArrow();
                return;
            }

            EnsureArrow();
            _arrowClock += Time.deltaTime;
            float bob = Mathf.Sin(Time.time * arrowBobSpeed) * arrowBobAmplitude;
            _arrow.transform.position = new Vector3(pos.x, pos.y + arrowHeadOffset + bob, pos.z - 0.1f);
            int idx = (int)(_arrowClock * Mathf.Max(1f, arrowFps)) % arrowFrames.Length;
            _arrowSr.sprite = arrowFrames[idx];
            if (!_arrow.activeSelf) _arrow.SetActive(true);
        }

        void EnsureArrow()
        {
            if (_arrow != null) return;
            _arrow = new GameObject("AbilityPointArrow");
            _arrow.transform.localScale = Vector3.one * arrowScale;
            _arrowSr = _arrow.AddComponent<SpriteRenderer>();
            _arrowSr.sortingOrder = arrowSortingOrder;
            _arrow.SetActive(false);
        }

        void HideArrow()
        {
            if (_arrow != null && _arrow.activeSelf) _arrow.SetActive(false);
        }

        // ---------- 능력 사용 피드백(대상 머리 위 1.2초 텍스트) ----------

        void ShowFloatingText(string targetId, string text, Color color)
        {
            if (!mansionView.TryGetTokenPosition(targetId, out var pos)) return;
            StartCoroutine(FloatingTextRoutine(pos, text, color));
        }

        IEnumerator FloatingTextRoutine(Vector3 pos, string text, Color color)
        {
            var go = new GameObject("AbilityFeedback");
            go.transform.position = new Vector3(pos.x, pos.y + arrowHeadOffset + 0.4f, pos.z - 0.1f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.fontSize = 42; tm.characterSize = 0.10f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sortingOrder = arrowSortingOrder + 1;
            yield return new WaitForSeconds(1.2f);
            if (go != null) Destroy(go);
        }

        // ---------- 안내/유틸 ----------

        void SetHint()
        {
            if (hintText == null) return;
            if (_candidates == null) { hintText.text = ""; return; }
            string near = FindNearestCandidate();
            string verb = AbilityVerb();
            hintText.text = near != null
                ? ("Space를 눌러 " + near + " " + verb)
                : ("대상 가까이 가서 Space로 " + verb);
        }

        string AbilityVerb()
        {
            var role = controller != null && controller.HumanPlayer != null ? controller.HumanPlayer.Role : Role.Citizen;
            switch (role)
            {
                case Role.Mafia: return "살해";
                case Role.Police: return "조사";
                case Role.Doctor: return "보호";
                default: return "지목";
            }
        }

        bool IsTyping()
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected == null) return false;
            return selected.GetComponent<TMP_InputField>() != null;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
