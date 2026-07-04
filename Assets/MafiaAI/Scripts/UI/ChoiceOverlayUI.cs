using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 투표 오버레이. 후보 카드를 클릭하면 선택(하이라이트)만 되고, "투표 확정"을 눌러야 실제로 제출된다.
    /// "기권" 버튼은 대상 없이 제출한다 — GameRules.ResolveVotes가 빈 대상을 이미 기권으로 처리한다.
    /// UI는 하이라키에 직접 배치해두고 이 스크립트는 참조만 한다(런타임에 UI를 코드로 만들지 않음).
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

        static readonly Color IdleColor = new Color32(0x33, 0x2A, 0x1E, 0xFF);
        static readonly Color SelectedColor = new Color32(0xC0, 0x39, 0x2B, 0xFF);

        const float HoverScale = 1.15f;   // 마우스 올린 카드
        const float RecedeScale = 0.88f;  // 그동안 나머지 카드
        const int HoverSortingOrder = 10; // 옆 카드 위로 튀어나와 보이게

        HumanActor _human;
        string _selected;
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

        void Update()
        {
            if (timerText == null || overlayRoot == null || !overlayRoot.activeSelf || controller == null) return;
            float remain = controller.PhaseEndsAt - Time.realtimeSinceStartup;
            timerText.text = remain > 0f
                ? string.Format("{0:00}:{1:00}", (int)remain / 60, (int)remain % 60)
                : "AI 생각 중…";
        }

        void OnEnable()
        {
            if (controller != null) controller.OnGameSetup += TryBindHuman;
        }

        void OnDisable()
        {
            if (controller != null) controller.OnGameSetup -= TryBindHuman;
            Unbind();
        }

        // GameController.Setup()에서 HumanActor가 만들어진 직후(OnGameSetup) 연결한다.
        void TryBindHuman()
        {
            Unbind();
            _human = controller.humanActor as HumanActor;
            if (_human == null) return; // 관전 모드(인간 좌석 없음)
            _human.OnNeedChoice += HandleNeedChoice;
            _human.OnChoiceResolved += HideOverlay;
        }

        void Unbind()
        {
            if (_human == null) return;
            _human.OnNeedChoice -= HandleNeedChoice;
            _human.OnChoiceResolved -= HideOverlay;
            _human = null;
        }

        void HandleNeedChoice(Player self, List<string> candidates, string kind)
        {
            // 밤 능력(마피아/의사/경찰) 대상 선택은 NightMovementUI가 담당한다. 이 오버레이는 투표에만 쓴다.
            if (kind != "vote") return;

            // 하이라키/프리팹을 아직 안 만들었으면 조용히 아무 것도 안 하고 넘어간다(사망 시간 초과로 자동 처리됨).
            if (overlayRoot == null || candidateListRoot == null || candidatePrefab == null) return;

            if (titleText != null) titleText.text = "투표 시간";
            _selected = null;

            // 나보다 먼저 투표를 마친 사람들의 표는 State.Votes에 이미 반영되어 있다.
            _tally.Clear();
            foreach (var v in controller.State.Votes.Values)
            {
                if (string.IsNullOrEmpty(v)) continue;
                _tally.TryGetValue(v, out int c);
                _tally[v] = c + 1;
            }

            foreach (Transform child in candidateListRoot) Destroy(child.gameObject);
            _cards.Clear();
            foreach (var id in candidates)
            {
                string target = id;
                var card = Instantiate(candidatePrefab, candidateListRoot);
                if (card.NameText != null) card.NameText.text = id;
                if (card.Portrait != null && portraits != null)
                {
                    var sprite = portraits.GetPortrait(id);
                    if (sprite != null) card.Portrait.sprite = sprite; // 없으면 기존 이미지(빈칸/기본값) 그대로 둠
                }
                if (card.Button != null) card.Button.onClick.AddListener(() => SelectCandidate(target));
                card.OnHoverChanged += HandleCardHover;
                _cards[id] = card;
            }

            RefreshCards();
            overlayRoot.SetActive(true);
        }

        void SelectCandidate(string id)
        {
            _selected = id;
            RefreshCards();
        }

        /// <summary>
        /// 마우스 올린 카드는 커지면서 다른 카드들 위로 튀어나오고, 나머지는 살짝 작아진다.
        /// Layout Group이 자식 크기를 다시 재는 건 sizeDelta/anchor 기준이라, 여기서 localScale만
        /// 바꾸는 건 레이아웃을 흔들지 않는다. 렌더 순서는 sibling index가 아니라 별도 Canvas로 덮어써서
        /// 카드 배치 순서를 안 건드리고도 위로 튀어나와 보이게 한다.
        /// </summary>
        void HandleCardHover(VoteCandidateView hovered, bool hovering)
        {
            foreach (var kv in _cards)
            {
                bool isHovered = kv.Value == hovered;
                kv.Value.SetTargetScale(hovering ? (isHovered ? HoverScale : RecedeScale) : 1f);
            }

            var canvas = hovered.GetComponent<Canvas>();
            if (canvas == null) canvas = hovered.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = hovering;
            canvas.sortingOrder = HoverSortingOrder;
        }

        int TallyOf(string id)
        {
            _tally.TryGetValue(id, out int c);
            return c;
        }

        void RefreshCards()
        {
            foreach (var kv in _cards)
            {
                bool mine = kv.Key == _selected;
                int count = TallyOf(kv.Key);
                if (kv.Value.VoteCountText != null)
                    kv.Value.VoteCountText.text = mine ? ("내 표 · " + count) : count.ToString();
                if (kv.Value.Background != null)
                    kv.Value.Background.color = mine ? SelectedColor : IdleColor;
            }

            if (statusText != null)
            {
                statusText.text = string.IsNullOrEmpty(_selected)
                    ? "처형할 대상을 고르세요"
                    : _selected + " 지목 중 · " + TallyOf(_selected) + "표 획득";
            }

            if (confirmButton != null) confirmButton.interactable = !string.IsNullOrEmpty(_selected);
        }

        void ConfirmVote()
        {
            if (string.IsNullOrEmpty(_selected)) return; // 대상 없이 확정은 막음(기권은 별도 버튼)
            _human.SubmitChoice(_selected);
        }

        void Abstain()
        {
            _human.SubmitChoice(null);
        }

        void HideOverlay()
        {
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }
    }
}
