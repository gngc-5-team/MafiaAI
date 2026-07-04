using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 투표 오버레이. 투표 페이즈 '전체' 동안 열려 있으면서, 표가 들어올 때마다 실시간으로 집계를 갱신한다.
    /// 내 차례가 되면 카드를 골라 "투표 확정"으로 제출하고, 제출 후에도 창은 닫히지 않고 남은 표를 지켜본다.
    /// 페이즈가 투표에서 벗어나면(새벽 등) 창을 닫는다.
    /// UI는 하이라키에 직접 배치해두고 이 스크립트는 참조만 한다.
    /// </summary>
    public class ChoiceOverlayUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] GameObject overlayRoot;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text timerText;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Transform candidateListRoot;
        [SerializeField] VoteCandidateView candidatePrefab;
        [SerializeField] Button confirmButton;
        [SerializeField] Button abstainButton;
        [SerializeField] PersonaPortraitLibrary portraits; // 이름→초상화 매핑. 안 만들었으면 비워둬도 동작

        static readonly Color IdleColor = new Color32(0x11, 0x10, 0x10, 0x00);
        static readonly Color DeadColor = new Color32(0x33, 0x33, 0x38, 0xFF); // 죽은 카드(회색, 초상화에 적용)

        const float HoverScale = 1.15f;   // 마우스 올린 카드
        const float RecedeScale = 0.88f;  // 그동안 나머지 카드
        const int HoverSortingOrder = 10; // 옆 카드 위로 튀어나와 보이게

        HumanActor _human;
        string _selected;
        bool _myTurn;    // 내(인간) 투표 차례가 되었는가
        bool _hasVoted;  // 이번 투표에서 이미 제출했는가
        readonly Dictionary<string, VoteCandidateView> _cards = new();
        readonly Dictionary<string, int> _tally = new();

        void Reset() => controller = GetComponent<GameController>();

        void Awake()
        {
            if (controller == null) controller = GetComponent<GameController>();
            if (controller == null) controller = FindFirstObjectByType<GameController>();
            if (overlayRoot != null) overlayRoot.SetActive(false);
            if (confirmButton != null) confirmButton.onClick.AddListener(ConfirmVote);
            if (abstainButton != null) abstainButton.onClick.AddListener(Abstain);
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnGameSetup += TryBindHuman;
            controller.OnPhaseChanged += HandlePhaseChanged;
            controller.OnVoteCast += RefreshTally;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnGameSetup -= TryBindHuman;
            controller.OnPhaseChanged -= HandlePhaseChanged;
            controller.OnVoteCast -= RefreshTally;
            Unbind();
        }

        void Update()
        {
            if (timerText == null || overlayRoot == null || !overlayRoot.activeSelf || controller == null) return;
            float remain = controller.PhaseEndsAt - Time.realtimeSinceStartup;
            timerText.text = remain > 0f
                ? string.Format("{0:00}:{1:00}", (int)remain / 60, (int)remain % 60)
                : "AI 생각 중…";
        }

        void TryBindHuman()
        {
            Unbind();
            _human = controller.humanActor as HumanActor;
            if (_human == null) return; // 관전 모드(인간 좌석 없음)
            _human.OnNeedChoice += HandleNeedChoice;
        }

        void Unbind()
        {
            if (_human == null) return;
            _human.OnNeedChoice -= HandleNeedChoice;
            _human = null;
        }

        void HandlePhaseChanged(GameState s)
        {
            if (s.Phase == Phase.Vote) OpenForVote();
            else HideOverlay();
        }

        /// <summary>투표 페이즈 진입 시 후보 카드를 만들고 창을 연다(내 차례가 아니어도 표를 지켜볼 수 있게).</summary>
        void OpenForVote()
        {
            if (overlayRoot == null || candidateListRoot == null || candidatePrefab == null) return;

            _selected = null;
            _myTurn = false;
            _hasVoted = false;
            if (titleText != null) titleText.text = "투표 시간";

            foreach (Transform child in candidateListRoot) Destroy(child.gameObject);
            _cards.Clear();

            // 전원(죽은 사람 포함)을 카드로 보여준다. 죽은 카드는 회색+X 처리하고 선택 불가.
            // 내 카드는 살아있어도 선택만 막는다(자기 자신 투표 불가).
            foreach (var p in controller.State.Players)
            {
                string id = p.Id;
                bool dead = !p.Alive;
                var card = Instantiate(candidatePrefab, candidateListRoot);
                if (card.NameText != null) card.NameText.text = id;
                if (card.Portrait != null && portraits != null)
                {
                    var sprite = portraits.GetPortrait(id);
                    if (sprite != null) card.Portrait.sprite = sprite;
                }

                bool selfCard = controller.HumanPlayer != null && id == controller.HumanPlayer.Id;
                bool selectable = !dead && !selfCard;
                if (selectable && card.Button != null) card.Button.onClick.AddListener(() => SelectCandidate(id));
                card.OnHoverChanged += HandleCardHover;
                card.SetDead(dead); // 죽었으면 X 오버레이 켜고 호버 잠금
                if (card.SelectedOverlay != null) card.SelectedOverlay.SetActive(false); // 시작은 선택 해제

                // 클릭/호버가 먹으려면 레이캐스트를 받는 그래픽이 필요하다(프리팹에서 꺼놨어도 여기서 보정).
                EnsureRaycast(card.GetComponent<Image>());
                EnsureRaycast(card.Background);
                if (card.Button != null) EnsureRaycast(card.Button.targetGraphic as Image);

                _cards[id] = card;
            }

            RefreshTally();
            overlayRoot.SetActive(true);
        }

        // 내(인간) 투표 차례가 되면 호출된다. 카드는 이미 떠 있고, 여기선 확정 가능 상태로만 전환.
        void HandleNeedChoice(Player self, List<string> candidates, string kind)
        {
            if (kind != "vote") return;
            _myTurn = true;
            RefreshTally();
        }

        void SelectCandidate(string id)
        {
            if (!_myTurn || _hasVoted) return; // 내 차례 아니면 선택 불가
            _selected = id;
            ApplyVisuals(null); // 선택한 카드가 (마우스를 떼도) 커진 채로 유지되게
            RefreshTally();
        }

        static void EnsureRaycast(Image img)
        {
            if (img != null) img.raycastTarget = true;
        }

        void HandleCardHover(VoteCandidateView hovered, bool hovering)
        {
            if (_hasVoted) return; // 투표 완료 후엔 호버 반응 없음
            ApplyVisuals(hovering ? hovered : null);
        }

        /// <summary>
        /// 카드 크기·전면(위로 튀어나옴)을 현재 상태에 맞춰 일괄 적용한다.
        /// - 마우스 올린 카드: 크게 + 앞으로, 나머지는 작게
        /// - 아무것도 안 올렸을 때: 선택한 카드만 크게 유지, 나머지는 기본
        /// - 죽은 카드/투표 완료 상태에선 크기 변화 없음
        /// </summary>
        void ApplyVisuals(VoteCandidateView hovered)
        {
            foreach (var kv in _cards)
            {
                var card = kv.Value;
                bool isSelected = kv.Key == _selected;

                // 잠긴 카드(죽음/투표완료)는 호버 반응 없음 — 단, 내가 고른 카드면 커진 채로 남긴다.
                if (card.HoverLocked)
                {
                    card.SetTargetScale(isSelected ? HoverScale : 1f);
                    SetFront(card, isSelected);
                    continue;
                }

                bool isHovered = hovered != null && card == hovered;
                float scale;
                if (hovered != null) scale = isHovered ? HoverScale : RecedeScale;
                else scale = isSelected ? HoverScale : 1f;

                card.SetTargetScale(scale);
                SetFront(card, isHovered || (hovered == null && isSelected));
            }
        }

        static void SetFront(VoteCandidateView card, bool front)
        {
            var canvas = card.GetComponent<Canvas>();
            if (canvas == null)
            {
                if (!front) return; // 앞으로 낼 필요 없으면 굳이 Canvas를 붙이지 않음
                canvas = card.gameObject.AddComponent<Canvas>();
                // 중첩 Canvas는 자체 GraphicRaycaster가 없으면 하위가 포인터 이벤트를 못 받아 호버가 굳는다.
                if (card.GetComponent<GraphicRaycaster>() == null)
                    card.gameObject.AddComponent<GraphicRaycaster>();
            }
            canvas.overrideSorting = front;
            canvas.sortingOrder = front ? HoverSortingOrder : 0;
        }

        int TallyOf(string id)
        {
            _tally.TryGetValue(id, out int c);
            return c;
        }

        /// <summary>현재까지 집계된 표를 State.Votes에서 다시 세어 카드에 반영한다(실시간 갱신).</summary>
        void RefreshTally()
        {
            if (_cards.Count == 0) return;

            _tally.Clear();
            foreach (var v in controller.State.Votes.Values)
            {
                if (string.IsNullOrEmpty(v)) continue;
                _tally.TryGetValue(v, out int c);
                _tally[v] = c + 1;
            }

            foreach (var kv in _cards)
            {
                var pl = controller.State.ById(kv.Key);
                bool dead = pl != null && !pl.Alive;
                bool mine = kv.Key == _selected;
                int count = TallyOf(kv.Key);
                if (kv.Value.VoteCountText != null)
                    kv.Value.VoteCountText.text = mine ? ("내 표 · " + count) : count.ToString();
                if (kv.Value.Background != null)
                    kv.Value.Background.color = IdleColor; // 선택 표시는 배경색이 아니라 체크 오버레이로 한다
                if (kv.Value.Portrait != null)
                    kv.Value.Portrait.color = dead ? DeadColor : Color.white;
                if (kv.Value.SelectedOverlay != null)
                    kv.Value.SelectedOverlay.SetActive(mine); // 내가 고른 카드에만 체크 표시
            }

            if (statusText != null)
            {
                if (_hasVoted)
                    statusText.text = "투표 완료 · 다른 사람 투표를 기다리는 중…";
                else if (!_myTurn)
                    statusText.text = "다른 사람이 투표 중…";
                else if (string.IsNullOrEmpty(_selected))
                    statusText.text = "처형할 대상을 고르세요";
                else
                    statusText.text = _selected + " 지목 중 · " + TallyOf(_selected) + "표 획득";
            }

            if (confirmButton != null) confirmButton.interactable = _myTurn && !_hasVoted && !string.IsNullOrEmpty(_selected);
            if (abstainButton != null) abstainButton.interactable = _myTurn && !_hasVoted;
        }

        void ConfirmVote()
        {
            if (!_myTurn || _hasVoted || string.IsNullOrEmpty(_selected)) return;
            _hasVoted = true;
            _myTurn = false;
            _human.SubmitChoice(_selected); // 창은 닫지 않는다 — 남은 표를 계속 지켜본다
            LockHoverAfterVote();
            RefreshTally();
        }

        void Abstain()
        {
            if (!_myTurn || _hasVoted) return;
            _hasVoted = true;
            _myTurn = false;
            _human.SubmitChoice(null);
            LockHoverAfterVote();
            RefreshTally();
        }

        /// <summary>투표 완료 후엔 호버 반응을 끈다. 내가 고른 카드는 커진 채로 남겨 "내가 찍은 표"를 보여준다.</summary>
        void LockHoverAfterVote()
        {
            ApplyVisuals(null); // 선택 카드만 크게 유지
            foreach (var card in _cards.Values) card.HoverLocked = true;
        }

        void HideOverlay()
        {
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }
    }
}
