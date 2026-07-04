using System.Collections.Generic;
using System.Linq;
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
            return EnsureUseful(Sanitize(raw, self.Id), s, self, null, null, SpeechPurpose.Statement);
        }

        public async Task<string> RebuttalAsync(GameState s, Player self, string presserId, string question, CancellationToken ct)
        {
            var p = PromptBuilder.Rebuttal(s, self, presserId, question);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return EnsureUseful(Sanitize(raw, self.Id), s, self, presserId, question, SpeechPurpose.Rebuttal);
        }

        public async Task<string> ReactAsync(GameState s, Player self, string speakerId, string statement, CancellationToken ct)
        {
            var p = PromptBuilder.React(s, self, speakerId, statement);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return EnsureUseful(Sanitize(raw, self.Id), s, self, speakerId, statement, SpeechPurpose.React);
        }

        public async Task<string> FreeTalkAsync(GameState s, Player self, CancellationToken ct)
        {
            var p = PromptBuilder.FreeTalk(s, self);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return EnsureUseful(Sanitize(raw, self.Id), s, self, null, null, SpeechPurpose.Statement);
        }

        public async Task<string> AskRoomQuestionAsync(GameState s, Player self, Player target, string room, string localTranscript, CancellationToken ct)
        {
            var p = PromptBuilder.RoomQuestion(s, self, target, room, localTranscript);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return EnsureUseful(Sanitize(raw, self.Id), s, self, target.Id, localTranscript, SpeechPurpose.Question);
        }

        public async Task<string> AnswerRoomQuestionAsync(GameState s, Player self, Player asker, string question, string room, string localTranscript, CancellationToken ct)
        {
            var p = PromptBuilder.RoomAnswer(s, self, asker, question, room, localTranscript);
            float temp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return EnsureUseful(Sanitize(raw, self.Id), s, self, asker.Id, question, SpeechPurpose.Answer);
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

        enum SpeechPurpose { Statement, Question, Answer, Rebuttal, React }

        string EnsureUseful(string line, GameState s, Player self, string otherId, string source, SpeechPurpose purpose)
        {
            line = Sanitize(line, self.Id);
            if (!IsWeak(line, purpose, otherId)) return line;

            switch (purpose)
            {
                case SpeechPurpose.Question:
                    return FallbackQuestion(self, otherId, source);
                case SpeechPurpose.Answer:
                case SpeechPurpose.Rebuttal:
                case SpeechPurpose.React:
                    return FallbackAnswer(self, otherId, source);
                default:
                    return FallbackStatement(s, self);
            }
        }

        static bool IsWeak(string line, SpeechPurpose purpose, string otherId)
        {
            if (string.IsNullOrWhiteSpace(line) || line == "...") return true;
            string t = line.Trim();
            string low = t.ToLowerInvariant();
            string[] banned =
            {
                "무슨 의미", "무슨 뜻", "무슨 말", "걱정스럽", "다행", "확인해주",
                "자세히 말", "좀 더 말", "잘 모르겠", "모르겠습니다", "질문에 답하겠습니다",
                "흥미롭", "좋은 질문", "그렇군", "그렇습니다", "동의합니다",
                "각자의 직업", "직업을 명확", "직업을 밝", "직업에 대한 질문",
                "상황의 불확실성을 줄", "답변을 드리겠습니다", "말씀에 옮겨짚",
                "명확하게 대답", "명확히 답변", "필요성을 느끼", "정말 궁금",
                "무슨 소리", "판단 하시는", "요청드립니다", "활동을 하셨습니까"
            };
            if (banned.Any(low.Contains)) return true;
            if (t.Length < 8) return true;
            if (purpose == SpeechPurpose.Question)
            {
                if (string.IsNullOrEmpty(otherId) || !t.Contains(otherId)) return true;
                if (!t.Contains("?") && !t.Contains("？")) return true;
            }
            return false;
        }

        string FallbackQuestion(Player self, string targetId, string localTranscript)
        {
            targetId = string.IsNullOrEmpty(targetId) ? PickAnyOtherName(self) : targetId;
            string clue = ExtractLastMeaningfulLine(localTranscript);
            if (ContainsRoleFishing(localTranscript))
                return RoleFishingQuestion(self, targetId);
            if (ContainsAlibiTopic(localTranscript))
                return AlibiQuestion(self, targetId);

            switch (self.Id)
            {
                case "카이":
                    return targetId + ", 방금 말 돌리지 말고 왜 그 타이밍에 그 얘길 꺼냈는지 똑바로 말해.";
                case "제로":
                    return targetId + ", 방금 발언과 이전 태도가 맞지 않습니다. 어느 쪽이 진짜 입장입니까?";
                case "미로":
                    return targetId + ", 방금 그 말 너무 깔끔한데요. 미리 준비한 변명은 아니죠?";
                case "하루":
                    return targetId + ", 나만 이상하게 느낀 거 아니죠? 방금 말이랑 전 말이 왜 달라요?";
                case "노아":
                    return targetId + ", 방금 흐름을 보면 누군가 의심을 돌리고 있어요. 그 중심에 왜 당신 말이 있죠?";
                case "세이":
                    return targetId + ", 방금 피했어. 이유 말해.";
                default:
                    return string.IsNullOrEmpty(clue)
                        ? targetId + ", 지금 누구를 가장 의심하는지 근거까지 말해봐."
                        : targetId + ", 방금 \"" + clue + "\" 이 말의 근거가 뭐야?";
            }
        }

        string FallbackAnswer(Player self, string otherId, string source)
        {
            otherId = string.IsNullOrEmpty(otherId) ? "너" : otherId;
            if (ContainsRoleFishing(source))
                return RoleFishingAnswer(self, otherId);
            if (ContainsAlibiTopic(source))
                return AlibiAnswer(self, otherId);

            switch (self.Id)
            {
                case "카이":
                    return "나 몰아가려는 건 좋은데, " + otherId + " 네 질문엔 근거가 없어.";
                case "제로":
                    return "제 입장은 분명합니다. 지금 이상한 건 제 답보다 " + otherId + "의 지목 근거가 비어 있다는 점입니다.";
                case "미로":
                    return "저를 찍는 건 편하죠, 그런데 " + otherId + " 씨가 왜 지금 저한테 시선을 고정하는지가 더 수상한데요?";
                case "하루":
                    return "아니, 난 숨긴 게 없어요. 오히려 " + otherId + "가 나한테만 몰아붙이는 이유가 이상해요.";
                case "노아":
                    return "이건 단순한 질문이 아니라 시선 돌리기일 수 있어요. " + otherId + "가 판을 먼저 짠 건 아닌지 봐야 합니다.";
                case "세이":
                    return "난 안 피했어. 근데 " + otherId + ", 너는 계속 찔러만 보고 있네.";
                default:
                    return "내 답은 명확해. 지금은 " + otherId + "의 지목 근거부터 검증해야 해.";
            }
        }

        string FallbackStatement(GameState s, Player self)
        {
            string target = s.Alive.Where(p => p.Id != self.Id).Select(p => p.Id).FirstOrDefault() ?? "누군가";
            string roleFisher = FindRecentRoleFisher(s, self);
            if (!string.IsNullOrEmpty(roleFisher))
                return RoleFishingAnswer(self, roleFisher);

            switch (self.Id)
            {
                case "카이":
                    return target + ", 너 아까부터 핵심은 피하고 있어. 난 그게 제일 수상해.";
                case "제로":
                    return target + "의 발언은 근거보다 방어가 먼저 나왔습니다. 그 순서가 수상합니다.";
                case "미로":
                    return target + " 씨, 너무 얌전하게 빠져나가려는 거 아니에요?";
                case "하루":
                    return target + "이 자꾸 말을 흐리는 게 이상해요. 그냥 누구를 의심하는지 말해요.";
                case "노아":
                    return target + "의 침묵이 우연처럼 보이지 않습니다. 누군가랑 입을 맞춘 걸 수도 있어요.";
                case "세이":
                    return target + " 말이 비었어. 난 거기 걸린다.";
                default:
                    return target + "의 말에서 근거가 빠졌어. 그 부분부터 확인해야 해.";
            }
        }

        string PickAnyOtherName(Player self)
        {
            return self.Id == "카이" ? "제로" : "카이";
        }

        static string ExtractLastMeaningfulLine(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return "";
            var lines = transcript.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (lines.Count == 0) return "";
            string last = lines[lines.Count - 1];
            int colon = last.IndexOf(':');
            if (colon >= 0 && colon + 1 < last.Length) last = last.Substring(colon + 1).Trim();
            if (last.Length > 34) last = last.Substring(0, 34).Trim() + "...";
            return last;
        }

        static bool ContainsRoleFishing(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Contains("직업") || text.Contains("역할") || text.Contains("경찰이야") ||
                   text.Contains("의사야") || text.Contains("정체") || text.Contains("직공");
        }

        static bool ContainsAlibiTopic(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Contains("어디") || text.Contains("동선") || text.Contains("있었") ||
                   text.Contains("활동") || text.Contains("봤어") || text.Contains("봤나") ||
                   text.Contains("누구랑") || text.Contains("방에");
        }

        string FindRecentRoleFisher(GameState s, Player self)
        {
            if (s == null) return null;
            for (int i = s.PublicLog.Count - 1; i >= 0 && i >= s.PublicLog.Count - 8; i--)
            {
                var e = s.PublicLog[i];
                if (e.Kind != LogKind.Speech || e.Speaker == self.Id) continue;
                if (ContainsRoleFishing(e.Text)) return e.Speaker;
            }
            return null;
        }

        string RoleFishingQuestion(Player self, string targetId)
        {
            targetId = string.IsNullOrEmpty(targetId) ? PickAnyOtherName(self) : targetId;
            switch (self.Id)
            {
                case "카이":
                    return targetId + ", 직업 캐묻는 거 경찰 찾는 마피아 무브잖아. 왜 지금 그걸 꺼냈어?";
                case "제로":
                    return targetId + ", 초반 직공 요구는 시민보다 마피아에게 유리합니다. 그 질문의 목적을 설명하십시오.";
                case "미로":
                    return targetId + " 씨, 직업 털자는 말 너무 달콤한데요. 경찰 찾으려는 건 아니고?";
                case "하루":
                    return targetId + ", 왜 지금 직업부터 까라고 해요? 그거 마피아한테 정보 주는 거잖아요.";
                case "노아":
                    return targetId + ", 직업 공개를 유도하는 순간 경찰과 의사가 드러납니다. 그걸 노린 건 아니죠?";
                case "세이":
                    return targetId + ", 직공 유도하지 마. 그거 마피아한테 좋아.";
                default:
                    return targetId + ", 왜 지금 직업 공개를 요구했는지부터 설명해.";
            }
        }

        string AlibiQuestion(Player self, string targetId)
        {
            targetId = string.IsNullOrEmpty(targetId) ? PickAnyOtherName(self) : targetId;
            switch (self.Id)
            {
                case "카이":
                    return targetId + ", 방금 어디 있었는지만 말하지 말고 누구랑 있었는지도 말해. 혼자였으면 그게 더 수상해.";
                case "제로":
                    return targetId + ", 동선을 시간순으로 말하십시오. 중간에 빈 시간이 있으면 그 부분을 의심하겠습니다.";
                case "미로":
                    return targetId + " 씨, 동선 예쁘게 포장하지 말고 누구랑 마주쳤는지부터 말해봐요.";
                case "하루":
                    return targetId + ", 어디 있었는지랑 누가 봤는지 같이 말해요. 혼자 있었다고 하면 믿기 어렵잖아요.";
                case "노아":
                    return targetId + ", 동선에 증인이 없으면 그 빈칸이 살해 타이밍이 됩니다. 누가 당신을 봤죠?";
                case "세이":
                    return targetId + ", 동선 말해. 증인 없으면 의심할게.";
                default:
                    return targetId + ", 어디 있었고 누가 봤는지 같이 말해.";
            }
        }

        string AlibiAnswer(Player self, string otherId)
        {
            otherId = string.IsNullOrEmpty(otherId) ? "너" : otherId;
            switch (self.Id)
            {
                case "카이":
                    return "내 동선 캐는 건 좋아. 근데 " + otherId + ", 너는 네 동선 먼저 안 깔고 왜 남부터 찔러?";
                case "제로":
                    return "동선 검증은 필요합니다. 다만 " + otherId + "도 같은 기준으로 본인 위치와 목격자를 말해야 공평합니다.";
                case "미로":
                    return "동선 물어보는 건 괜찮죠. 그런데 " + otherId + " 씨가 자기 동선은 숨기고 남만 캐면 그게 더 냄새납니다.";
                case "하루":
                    return "내 동선은 말할 수 있어요. 대신 " + otherId + "도 어디 있었는지 같이 말해야죠.";
                case "노아":
                    return "동선 공개는 서로 대칭이어야 합니다. " + otherId + "만 질문하고 자기 위치를 숨기면 그게 더 큰 단서예요.";
                case "세이":
                    return "동선 깔 수 있어. 근데 " + otherId + "도 같이 까.";
                default:
                    return "동선 검증은 좋아. 대신 " + otherId + "도 같은 기준으로 말해야 해.";
            }
        }

        string RoleFishingAnswer(Player self, string otherId)
        {
            otherId = string.IsNullOrEmpty(otherId) ? "너" : otherId;
            switch (self.Id)
            {
                case "카이":
                    return "직업부터 까자는 건 마피아한테 밥 주는 거야. " + otherId + ", 너 왜 경찰 찾는 질문을 해?";
                case "제로":
                    return "지금 직업 공개는 시민 이득보다 마피아 이득이 큽니다. " + otherId + "의 질문 의도부터 검증해야 합니다.";
                case "미로":
                    return "직업은 그렇게 쉽게 안 까죠. " + otherId + " 씨가 왜 그 정보를 먼저 원했는지가 더 재밌는데요?";
                case "하루":
                    return "아니, 지금 직업 까면 경찰이나 의사만 위험해져요. " + otherId + "가 그걸 모른 척하는 게 이상해요.";
                case "노아":
                    return "직공 유도는 정보 수집입니다. " + otherId + "가 시민을 돕는 척하면서 역할을 골라내는 걸 수도 있어요.";
                case "세이":
                    return "직업 안 까. " + otherId + ", 그 질문이 더 수상해.";
                default:
                    return "직업 공개는 지금 이득이 적어. 먼저 " + otherId + "의 질문 의도부터 봐야 해.";
            }
        }
    }
}
