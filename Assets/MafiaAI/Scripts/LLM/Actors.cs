using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MafiaAI.Core;

namespace MafiaAI.LLM
{
    /// <summary>한 좌석(AI 또는 인간)의 의사결정 인터페이스.</summary>
    public interface IActor
    {
        Task<string> SpeakAsync(GameState s, Player self, CancellationToken ct);
        Task<ActionChoice> VoteAsync(GameState s, Player self, CancellationToken ct);
        Task<ActionChoice> NightAsync(GameState s, Player self, CancellationToken ct);
        Task<string> RebuttalAsync(GameState s, Player self, string presserId, string question, CancellationToken ct);
        Task<string> ReactAsync(GameState s, Player self, string speakerId, string statement, CancellationToken ct);
        Task<string> FreeTalkAsync(GameState s, Player self, CancellationToken ct);
        Task<string> AskRoomQuestionAsync(GameState s, Player self, Player target, string room, string localTranscript, CancellationToken ct);
        Task<string> AnswerRoomQuestionAsync(GameState s, Player self, Player asker, string question, string room, string localTranscript, CancellationToken ct);
    }

    /// <summary>Gemma로 구동되는 AI 좌석.</summary>
    public sealed class AIActor : IActor
    {
        readonly OllamaClient _ollama;
        readonly GameConfig _cfg;
        readonly IRng _rng;

        public AIActor(OllamaClient ollama, GameConfig cfg, IRng rng)
        {
            _ollama = ollama;
            _cfg = cfg;
            _rng = rng;
        }

        public async Task<string> SpeakAsync(GameState s, Player self, CancellationToken ct)
        {
            var p = PromptBuilder.Discussion(s, self);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return Sanitize(raw, self.Id);
        }

        public async Task<string> RebuttalAsync(GameState s, Player self, string presserId, string question, CancellationToken ct)
        {
            var p = PromptBuilder.Rebuttal(s, self, presserId, question);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return Sanitize(raw, self.Id);
        }

        public async Task<string> ReactAsync(GameState s, Player self, string speakerId, string statement, CancellationToken ct)
        {
            var p = PromptBuilder.React(s, self, speakerId, statement);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return Sanitize(raw, self.Id);
        }

        public async Task<string> FreeTalkAsync(GameState s, Player self, CancellationToken ct)
        {
            var p = PromptBuilder.FreeTalk(s, self);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return Sanitize(raw, self.Id);
        }

        public async Task<string> AskRoomQuestionAsync(GameState s, Player self, Player target, string room, string localTranscript, CancellationToken ct)
        {
            var p = PromptBuilder.RoomQuestion(s, self, target, room, localTranscript);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return Sanitize(raw, self.Id);
        }

        public async Task<string> AnswerRoomQuestionAsync(GameState s, Player self, Player asker, string question, string room, string localTranscript, CancellationToken ct)
        {
            var p = PromptBuilder.RoomAnswer(s, self, asker, question, room, localTranscript);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return Sanitize(raw, self.Id);
        }

        public async Task<ActionChoice> VoteAsync(GameState s, Player self, CancellationToken ct)
        {
            var p = PromptBuilder.Vote(s, self);
            var valid = PromptBuilder.VoteCandidates(s, self);
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, _cfg.ActionTemperature, true, ct);
            return Resolve(raw, valid);
        }

        public async Task<ActionChoice> NightAsync(GameState s, Player self, CancellationToken ct)
        {
            var p = PromptBuilder.Night(s, self);
            var valid = NightCandidates(s, self);
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, _cfg.ActionTemperature, true, ct);
            return Resolve(raw, valid);
        }

        List<string> NightCandidates(GameState s, Player self)
        {
            switch (self.Role)
            {
                case Role.Mafia: return PromptBuilder.MafiaTargets(s, self);
                case Role.Doctor:
                {
                    var list = new List<string>();
                    foreach (var pl in s.Alive) list.Add(pl.Id);
                    return list;
                }
                default: return PromptBuilder.OthersAlive(s, self);
            }
        }

        /// <summary>파싱 실패 시 유효 후보 중 랜덤으로 폴백(기권 방지).</summary>
        ActionChoice Resolve(string raw, List<string> valid)
        {
            var choice = ActionParser.Parse(raw, valid);
            if (!choice.Valid && valid.Count > 0)
            {
                choice.TargetId = valid[_rng.Next(valid.Count)];
                choice.Reason = "(판단 폴백)";
            }
            return choice;
        }

        /// <summary>모델이 붙이는 이름표/따옴표/개행을 정리.</summary>
        static string Sanitize(string raw, string selfId)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "...";
            string t = raw.Trim();
            // 맨 앞 "이름:" 형태 제거
            if (t.StartsWith(selfId + ":")) t = t.Substring(selfId.Length + 1).Trim();
            if (t.StartsWith(selfId + " :")) t = t.Substring(selfId.Length + 2).Trim();
            t = t.Trim('"', '\'', '“', '”');
            t = t.Replace("\n", " ").Replace("\r", " ").Trim();
            return string.IsNullOrEmpty(t) ? "..." : t;
        }
    }
}
