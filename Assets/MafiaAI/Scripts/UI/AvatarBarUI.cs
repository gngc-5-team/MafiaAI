using System.Collections.Generic;
using UnityEngine;
using MafiaAI.Core;
using MafiaAI.LLM;

namespace MafiaAI.UI
{
    /// <summary>
    /// 하이라키에 직접 배치한 생존자 목록 컨테이너(Vertical Layout Group 붙은 빈 오브젝트)에
    /// 플레이어 수만큼 AvatarCardView 프리팹을 찍고 생존/사망 상태를 갱신한다.
    /// </summary>
    [RequireComponent(typeof(GameController))]
    public class AvatarBarUI : MonoBehaviour
    {
        [SerializeField] GameController controller;
        [SerializeField] Transform cardListRoot;
        [SerializeField] AvatarCardView cardPrefab;

        static readonly Color PanelColor = new Color32(0x1E, 0x1E, 0x2A, 0xFF);
        static readonly Color AccentColor = new Color32(0xC0, 0x39, 0x2B, 0xFF);
        static readonly Color DeadBgColor = new Color32(0x10, 0x10, 0x14, 0xFF);
        static readonly Color NameColor = new Color32(0xE0, 0xA0, 0x30, 0xFF);
        static readonly Color TextColor = new Color32(0xE8, 0xE8, 0xEC, 0xFF);
        static readonly Color DimColor = new Color32(0x8A, 0x8A, 0x99, 0xFF);
        static readonly Color DeathColor = new Color32(0xE0, 0x50, 0x3A, 0xFF);

        readonly Dictionary<string, AvatarCardView> _cards = new();

        void Reset() => controller = GetComponent<GameController>();
        void Awake() { if (controller == null) controller = GetComponent<GameController>(); }

        void OnEnable()
        {
            if (controller == null) return;
            controller.OnGameSetup += BuildCards;
            controller.OnLog += HandleLog;
            controller.OnPhaseChanged += HandlePhaseChanged;
        }

        void OnDisable()
        {
            if (controller == null) return;
            controller.OnGameSetup -= BuildCards;
            controller.OnLog -= HandleLog;
            controller.OnPhaseChanged -= HandlePhaseChanged;
        }

        void HandleLog(LogEntry e) => RefreshCards();
        void HandlePhaseChanged(GameState s) => RefreshCards();

        void BuildCards()
        {
            if (cardListRoot == null || cardPrefab == null) return;
            foreach (Transform c in cardListRoot) Destroy(c.gameObject);
            _cards.Clear();
            foreach (var p in controller.State.Players)
                _cards[p.Id] = Instantiate(cardPrefab, cardListRoot);
            RefreshCards();
        }

        void RefreshCards()
        {
            if (controller.State == null) return;
            foreach (var p in controller.State.Players)
            {
                if (!_cards.TryGetValue(p.Id, out var card)) continue;
                if (p.Alive)
                {
                    card.nameText.text = p.Id + (p.IsHuman ? " (나)" : "");
                    card.nameText.color = p.IsHuman ? NameColor : TextColor;
                    card.statusText.text = "생존";
                    card.statusText.color = DimColor;
                    card.background.color = p.IsHuman ? Color.Lerp(PanelColor, AccentColor, 0.4f) : PanelColor;
                }
                else
                {
                    card.nameText.color = DimColor;
                    card.statusText.text = "사망 · " + p.Role.Korean();
                    card.statusText.color = DeathColor;
                    card.background.color = DeadBgColor;
                }
            }
        }
    }
}
