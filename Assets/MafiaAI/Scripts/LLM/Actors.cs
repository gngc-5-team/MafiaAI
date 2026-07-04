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
        Task<string> InterrogateAsync(GameState s, Player self, string playerId, string question, string history, CancellationToken ct);
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

        public async Task<string> InterrogateAsync(GameState s, Player self, string playerId, string question, string history, CancellationToken ct)
        {
            var p = PromptBuilder.Interrogation(s, self, playerId, question, history);
            // 심문은 방어가 느슨한 상황이라 온도를 살짝 올려 말실수/흔들림이 나오기 쉽게 한다.
            float baseTemp = self.Temperature > 0 ? self.Temperature : _cfg.SpeechTemperature;
            float temp = baseTemp + 0.1f;
            if (temp > 1.3f) temp = 1.3f;
            string raw = await _ollama.GenerateAsync(_cfg.Model, p.User, p.System, temp, false, ct);
            return EnsureUseful(Sanitize(raw, self.Id), s, self, playerId, question, SpeechPurpose.Answer);
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
                    return PromptBuilder.DoctorTargets(s, self);
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
            if (purpose == SpeechPurpose.Question && !string.IsNullOrEmpty(otherId) && !line.Contains(otherId))
                line = otherId + ", " + line;

            if (!NeedsRepair(line)) return line;

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

        static bool NeedsRepair(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line == "...") return true;
            string t = line.Trim();
            string low = t.ToLowerInvariant();
            string[] broken =
            {
                "ai", "인공지능", "언어 모델", "프롬프트", "시스템 지시", "개발자",
                "json", "target", "reason", "질문에 답하겠습니다", "답변을 드리겠습니다",
                "명확히 답변", "명확하게 대답", "필요성을 느낍니다", "상황의 불확실성을 줄"
            };
            if (broken.Any(low.Contains)) return true;
            if (t.Length < 3) return true;
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
                    return targetId + ", 너 말이야. 촉이 안 좋아. 뭐라도 말해봐, 들어보고 정할게.";
                case "제로":
                    return targetId + ", 어제와 오늘 발언을 비교하면 어긋나는 지점이 있습니다. 본인 입으로 정리해 보시죠.";
                case "미로":
                    return targetId + "~ 심심한데 우리 게임 하나 할까? 네가 마피아면 뭐부터 할 건지 말해봐.";
                case "하루":
                    return targetId + ", 저기… 다들 무섭게 구는데, 넌 아니지? 아니라고 해줘요.";
                case "노아":
                    return targetId + ", 당신의 어젯밤 침묵이 제 그림의 빈칸과 정확히 겹칩니다. 설명해 주시겠어요?";
                case "세이":
                    return targetId + ". …할 말 있으면 해.";
                default:
                    return string.IsNullOrEmpty(clue)
                        ? targetId + ", 넌 지금 누구 편이야?"
                        : targetId + ", 방금 \"" + clue + "\" 그거 무슨 뜻이야?";
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
                    return "웃기네. " + otherId + ", 나 건드리지 말고 네 걱정이나 해.";
                case "제로":
                    return "질문의 전제부터 틀렸습니다. " + otherId + " 씨, 제 발언 기록 어디에 그런 내용이 있었죠?";
                case "미로":
                    return "오~ 나 지목당한 거야? 영광인데? " + otherId + ", 근데 너 지금 표정 관리 안 되는 거 알아?";
                case "하루":
                    return "네?! 저 아니에요… 진짜예요. " + otherId + "까지 절 그렇게 보면 저 어떡해요.";
                case "노아":
                    return "저를 지목하는 것도 예상 범위입니다. " + otherId + ", 당신이 그 말을 하도록 유도된 걸 수도 있어요.";
                case "세이":
                    return "아니야. …끝.";
                default:
                    return "그건 아니야. " + otherId + ", 넌 왜 그렇게 생각했는데?";
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
                    return "난 " + target + "이(가) 제일 걸려. 촉이야. 오늘 얘 좀 보자.";
                case "제로":
                    return "지금까지의 발언을 정리하면 " + target + "의 위치가 가장 설명이 안 됩니다. 반박은 근거로 하십시오.";
                case "미로":
                    return "심심하다~ " + target + ", 네가 마피아라고 치고 얘기 짜보자. 의외로 맞을지도?";
                case "하루":
                    return "방금 얘기 듣고 보니… " + target + "이(가) 좀 걸리는 것 같기도 해요. 다들 어떻게 생각해요?";
                case "노아":
                    return "제 그림에서는 " + target + "이(가) 중심에 있습니다. 어제부터의 흐름이 전부 그쪽으로 모여요.";
                case "세이":
                    return "몰라. 굳이 고르면 " + target + ".";
                default:
                    return target + " 얘기 좀 해보자. 다들 어떻게 봐?";
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
                    return targetId + ", 직업 타령할 시간에 똑바로 서. 넌 그 질문부터가 재수없어.";
                case "제로":
                    return targetId + ", 초반 직업 공개 요구는 통계적으로 마피아에게 유리합니다. 질문의 목적을 설명하십시오.";
                case "미로":
                    return "직업 공개? 좋지~ " + targetId + " 너부터 까. 아, 싫어? 그럼 왜 시켰어?";
                case "하루":
                    return targetId + "… 그거 물어봐도 되는 거예요? 왠지 무서운데…";
                case "노아":
                    return targetId + ", 직업을 물은 그 순간이 제 그림의 시작점입니다. 경찰을 찾고 있죠, 지금?";
                case "세이":
                    return "안 까. " + targetId + ", 너나 까.";
                default:
                    return targetId + ", 그 질문 왜 했어?";
            }
        }

        string AlibiQuestion(Player self, string targetId)
        {
            targetId = string.IsNullOrEmpty(targetId) ? PickAnyOtherName(self) : targetId;
            switch (self.Id)
            {
                case "카이":
                    return targetId + ", 동선이고 뭐고 얼굴 보고 말해. 너 지금 떨고 있잖아.";
                case "제로":
                    return targetId + ", 동선을 시간순으로 말하십시오. 빈 시간이 있으면 그 구간을 의심하겠습니다.";
                case "미로":
                    return targetId + "~ 어디 있었는지 맞혀볼까? 틀리면 네가 말해주기. 콜?";
                case "하루":
                    return targetId + "이(가) 어디 있었는지 누가 봤어요? 봤다는 사람 있으면 전 믿을래요.";
                case "노아":
                    return targetId + ", 당신 동선의 빈칸이 제 서사의 잃어버린 조각과 일치합니다. 우연일까요?";
                case "세이":
                    return targetId + ", 어디 있었어. …궁금해서는 아니고.";
                default:
                    return targetId + ", 어디 있었어?";
            }
        }

        string AlibiAnswer(Player self, string otherId)
        {
            otherId = string.IsNullOrEmpty(otherId) ? "너" : otherId;
            switch (self.Id)
            {
                case "카이":
                    return "하, 나 취조하냐? " + otherId + ", 기억 안 나. 됐어?";
                case "제로":
                    return "말씀드리죠. 다만 " + otherId + "도 같은 기준으로 본인 위치를 말해야 공평합니다.";
                case "미로":
                    return "내 동선? 비밀~ 이라고 하면 화낼 거지? " + otherId + " 반응 보고 싶었어.";
                case "하루":
                    return "저요? 어… 기억이 잘… 아, 맞다, 방에 있었어요! 진짜예요, " + otherId + " 믿어줘요.";
                case "노아":
                    return "제 동선을 물으셨군요. 좋습니다, 그것도 기록해 두죠. " + otherId + "의 질문 순서까지 전부 자료입니다.";
                case "세이":
                    return "있던 데 있었어.";
                default:
                    return "말해줄게. 대신 " + otherId + "도 말해.";
            }
        }

        string RoleFishingAnswer(Player self, string otherId)
        {
            otherId = string.IsNullOrEmpty(otherId) ? "너" : otherId;
            switch (self.Id)
            {
                case "카이":
                    return "내 직업? 알 거 없어. " + otherId + ", 너 그 질문 하는 순간부터 내 리스트에 올랐어.";
                case "제로":
                    return "공개하지 않겠습니다. 지금 직업 공개는 시민 이득보다 마피아 이득이 큽니다. 그게 답의 전부입니다.";
                case "미로":
                    return "나? 백수야~ 아 게임 안에서? 그건 비밀이지, " + otherId + " 넌 뭔데?";
                case "하루":
                    return "저… 말해도 돼요? 아, 안 되는 거구나… 미안해요, " + otherId + ", 말 못 해요.";
                case "노아":
                    return "제 역할을 궁금해하는 사람이 나타났다 — 이것도 그림의 일부입니다. " + otherId + ", 기록해 두겠습니다.";
                case "세이":
                    return "안 알려줘.";
                default:
                    return "그건 말 안 할래. " + otherId + ", 왜 궁금한데?";
            }
        }
    }
}
