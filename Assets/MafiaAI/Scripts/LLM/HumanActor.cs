using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MafiaAI.Core;

namespace MafiaAI.LLM
{
    /// <summary>
    /// 인간 좌석. 발언/투표/밤 능력이 필요할 때 UI에 요청하고,
    /// UI가 Submit* 을 호출하면 대기 중이던 Task가 완료된다.
    /// </summary>
    public sealed class HumanActor : IActor
    {
        /// <summary>UI가 구독: 발언 입력이 필요하다.</summary>
        public event Action<Player> OnNeedSpeech;

        /// <summary>UI가 구독: 대상 선택이 필요하다. (self, 후보들, kind: vote|mafia|police|doctor)</summary>
        public event Action<Player, List<string>, string> OnNeedChoice;

        /// <summary>UI가 구독: 선택이 (클릭이든 시간초과든) 확정되었다 → 오버레이 닫기.</summary>
        public event Action OnChoiceResolved;

        TaskCompletionSource<string> _speechTcs;
        TaskCompletionSource<ActionChoice> _choiceTcs;

        /// <summary>직전 발언이 특정 인물을 지목 추궁했다면 그 대상 id, 아니면 null.</summary>
        public string LastPressTarget { get; private set; }

        public Task<string> SpeakAsync(GameState s, Player self, CancellationToken ct)
        {
            LastPressTarget = null;
            _speechTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => _speechTcs.TrySetCanceled());
            OnNeedSpeech?.Invoke(self);
            return _speechTcs.Task;
        }

        // 인간은 추궁/반응 대상이 되지 않는다(플레이어는 1명).
        public Task<string> RebuttalAsync(GameState s, Player self, string presserId, string question, CancellationToken ct)
            => Task.FromResult("...");

        public Task<string> ReactAsync(GameState s, Player self, string speakerId, string statement, CancellationToken ct)
            => Task.FromResult("...");

        public Task<string> FreeTalkAsync(GameState s, Player self, CancellationToken ct)
            => Task.FromResult("...");

        public Task<string> AskRoomQuestionAsync(GameState s, Player self, Player target, string room, string localTranscript, CancellationToken ct)
            => Task.FromResult("...");

        public Task<string> AnswerRoomQuestionAsync(GameState s, Player self, Player asker, string question, string room, string localTranscript, CancellationToken ct)
            => Task.FromResult("...");

        /// <summary>밤 제한시간 초과 시 컨트롤러가 자동 대상으로 대기 중인 선택을 마감.</summary>
        public void ForceResolveChoice(string targetId)
        {
            _choiceTcs?.TrySetResult(new ActionChoice { TargetId = targetId, Reason = "(시간 초과 자동선택)" });
            OnChoiceResolved?.Invoke();
        }

        public Task<ActionChoice> VoteAsync(GameState s, Player self, CancellationToken ct)
        {
            var cands = PromptBuilder.VoteCandidates(s, self);
            return RequestChoice(self, cands, "vote", ct);
        }

        public Task<ActionChoice> NightAsync(GameState s, Player self, CancellationToken ct)
        {
            List<string> cands;
            string kind;
            switch (self.Role)
            {
                case Role.Mafia: cands = PromptBuilder.MafiaTargets(s, self); kind = "mafia"; break;
                case Role.Doctor:
                    cands = new List<string>();
                    foreach (var p in s.Alive) cands.Add(p.Id);
                    kind = "doctor";
                    break;
                default: cands = PromptBuilder.OthersAlive(s, self); kind = "police"; break;
            }
            return RequestChoice(self, cands, kind, ct);
        }

        Task<ActionChoice> RequestChoice(Player self, List<string> cands, string kind, CancellationToken ct)
        {
            _choiceTcs = new TaskCompletionSource<ActionChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => _choiceTcs.TrySetCanceled());
            OnNeedChoice?.Invoke(self, cands, kind);
            return _choiceTcs.Task;
        }

        // ---- UI가 호출 ----
        public void SubmitSpeech(string text)
            => _speechTcs?.TrySetResult(string.IsNullOrWhiteSpace(text) ? "..." : text.Trim());

        /// <summary>발언 + 지목 추궁 대상(없으면 null). 대상 AI가 즉시 반박 턴을 갖는다.</summary>
        public void SubmitSpeech(string text, string pressTarget)
        {
            LastPressTarget = string.IsNullOrEmpty(pressTarget) ? null : pressTarget;
            SubmitSpeech(text);
        }

        public void SubmitChoice(string targetId)
        {
            _choiceTcs?.TrySetResult(new ActionChoice { TargetId = targetId, Reason = "(당신의 선택)" });
            OnChoiceResolved?.Invoke();
        }
    }
}
