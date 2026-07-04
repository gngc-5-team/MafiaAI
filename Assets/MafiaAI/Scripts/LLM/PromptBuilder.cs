using System.Collections.Generic;
using System.Linq;
using System.Text;
using MafiaAI.Core;

namespace MafiaAI.LLM
{
    public readonly struct Prompt
    {
        public readonly string System;
        public readonly string User;
        public Prompt(string system, string user) { System = system; User = user; }
    }

    /// <summary>
    /// 매 턴 SYSTEM(인격+역할) + CONTEXT(공개) + PRIVATE(비밀) + TASK(지시)를 재조립.
    /// 기획서 6.1의 프롬프트 구조를 그대로 구현.
    /// </summary>
    public static class PromptBuilder
    {
        const int RecentLogLines = 16;

        public static Prompt Discussion(GameState s, Player self)
        {
            string task =
                "[지시] 지금은 낮 토론이다. 네 페르소나답게 한 번 발언하라.\n" +
                "- 행동 후보(네 성격에 맞는 걸 골라라): 감으로 지목, 떠보기, 변호, 동조, 농담으로 판 흔들기, 모순 짚기, 여론 따라가기, 심드렁하게 흘리기, 자기 서사 펼치기.\n" +
                "- 직전에 나온 발언과 같은 화제·같은 요구를 반복하지 마라. 대화를 네 색깔로 꺾어라.\n" +
                "- 말은 짧게. 1~2문장, 이름표 없이.";
            return new Prompt(BuildSystem(s, self), BuildUser(s, self, task));
        }

        /// <summary>자유 토론: 최근 대화를 읽고 자기가 중요하다고 생각하는 것에 자율 반응.</summary>
        public static Prompt FreeTalk(GameState s, Player self)
        {
            string task =
                "[지시] 지금은 자유 토론 중이다. 최근 대화에서 네가 가장 중요하다고 느낀 지점에 반응하라.\n" +
                "- 누군가 방금 너(" + self.Id + ")에게 말했거나 너를 지목했다면 그 말부터 받아라.\n" +
                "- 아니라면 네 성격대로 판을 움직여라: 감지목, 떠보기, 변호, 동조, 농담, 역질문, 투표선 흔들기, 심드렁 — 뭐든 너답게.\n" +
                "- 정보가 없어도 억지로 검증 질문을 만들 필요 없다. 네 캐릭터라면 이 침묵의 순간에 뭐라고 할까?\n" +
                "- 1~2문장, 이름표 없이.";
            return new Prompt(BuildSystem(s, self), BuildUser(s, self, task));
        }

        /// <summary>방금 나온 특정 발언에 한 인물이 직접 반응하는 턴(무시 방지).</summary>
        public static Prompt React(GameState s, Player self, string speakerId, string statement)
        {
            string task =
                "[반응] 방금 " + speakerId + "이(가) 너에게 이렇게 말했다: \"" + statement + "\"\n" +
                "그 말을 네 입장에서 해석하고 반응하라.\n" +
                "- 이 말이 너에게 위험한지, 상대에게 이득인지, 판을 흔들 기회인지 판단하라.\n" +
                "- 직업 공개 요구라면 바로 공개할지 숨길지 네 역할과 위험도를 보고 정하되, 대체로 초반 직공은 마피아에게 정보가 된다.\n" +
                "- 의심·추궁이면 방어만 하지 말고 상대의 의도나 논리 빈틈도 볼 수 있다.\n" +
                "- 1~2문장, 이름표 없이.";
            return new Prompt(BuildSystem(s, self), BuildUser(s, self, task));
        }

        /// <summary>인간이 특정 인물을 지목해 말을 걸었을 때, 그 대상이 즉시 반응하는 턴.</summary>
        public static Prompt Rebuttal(GameState s, Player self, string presserId, string question)
        {
            string task =
                "[지목] " + presserId + "이(가) 너를 콕 집어 말을 걸었다: \"" + question + "\"\n" +
                "모두가 지금 너를 본다. 네가 이 상황에서 살려면 어떤 말이 이득인지 판단해 답하라.\n" +
                "- 직업 공개 요구, 알리바이 요구, 감정적 몰이, 논리 검증 중 무엇인지 먼저 파악하라.\n" +
                "- 필요하면 답변, 회피, 역질문, 반격, 부분 공개 중 하나를 골라라. 단 게임 룰 밖 정보는 말하지 마라.\n" +
                "- 1~2문장, 이름표 없이.";
            return new Prompt(BuildSystem(s, self), BuildUser(s, self, task));
        }


        /// <summary>같은 방의 한 사람을 지목해 묻는 1:1 질문. 같은 방 사람들은 이 질문을 듣는다.</summary>
        public static Prompt RoomQuestion(GameState s, Player self, Player target, string room, string localTranscript)
        {
            string task =
                "[방 대화] 지금 너는 '" + room + "'에 있고, 같은 방 사람들만 네 말을 듣는다.\n" +
                "[같은 방 최근 대화]\n" + (string.IsNullOrWhiteSpace(localTranscript) ? "  (아직 들은 말 없음)" : localTranscript) + "\n" +
                "[지시] " + target.Id + "에게 1:1로 말을 걸어라. 방 안의 다른 사람들은 엿들을 수 있다.\n" +
                "- " + target.Id + "의 이름을 넣어라. 그래야 누가 질문받는지 보인다.\n" +
                "- 질문은 네 목적이 드러나야 한다: 정보 캐기, 떠보기, 압박, 회유, 말 바꿈 확인, 직공 유도 의심 등.\n" +
                "- 같은 질문 반복보다, 방금 대화의 빈틈을 물어라.\n" +
                "- 1문장, 이름표 없이.";
            return new Prompt(BuildSystem(s, self), BuildRoomUser(s, self, room, localTranscript, task));
        }

        /// <summary>같은 방에서 받은 1:1 질문에 답변. 같은 방 사람들은 이 답을 듣는다.</summary>
        public static Prompt RoomAnswer(GameState s, Player self, Player asker, string question, string room, string localTranscript)
        {
            string task =
                "[방 대화] 지금 너는 '" + room + "'에 있고, 같은 방 사람들만 네 말을 듣는다.\n" +
                "[같은 방 최근 대화]\n" + (string.IsNullOrWhiteSpace(localTranscript) ? "  (아직 들은 말 없음)" : localTranscript) + "\n" +
                "[질문] " + asker.Id + "이(가) 너에게 물었다: \"" + question + "\"\n" +
                "[지시] 이 질문이 너에게 이득인지 손해인지 판단하고 답하라.\n" +
                "- 사실대로 말할지, 일부만 말할지, 상대 의도를 찌를지, 다른 사람의 발언으로 시선을 돌릴지 네 역할과 성격대로 고른다.\n" +
                "- 질문을 피한다면 왜 피하는지 게임 안의 이유가 있어야 한다.\n" +
                "- 1~2문장, 이름표 없이.";
            return new Prompt(BuildSystem(s, self), BuildRoomUser(s, self, room, localTranscript, task));
        }

        /// <summary>
        /// 플레이어가 방에서 대상을 따로 불러내 벌이는 1:1 비공개 심문(플레이어 전용 기능).
        /// 다른 사람은 이 대화를 듣지 못한다. 집중 압박 상황이라 대상의 방어가 평소보다 느슨하다:
        /// 말실수·모순·부분 인정 빈도가 오르고, 플레이어의 논리가 타당하면 평소보다 쉽게 흔들린다.
        /// 단 페르소나와 게임 룰(메타 금지, 진짜 밤 정보는 시스템이 준 것만)은 그대로 유지.
        /// </summary>
        public static Prompt Interrogation(GameState s, Player self, string playerId, string question, string history)
        {
            string roleHint = self.Role == Role.Mafia
                ? "- 너는 마피아다. 단둘의 압박이라 완벽하게 방어하려 들지 마라. 상대 논리가 타당하면 말끝이 흔들리거나, 사소한 모순·회피·과한 변명이 새어나와도 좋다(완전 자백까진 아니어도 빈틈을 보여라)."
                : "- 너는 시민 진영이라 결백하다. 위축될 필요는 없지만, 확신 없는 부분은 솔직히 '모른다'고 하거나 얼버무릴 수 있다.";
            string task =
                "[단둘의 심문] 지금 너는 '" + playerId + "'에게 따로 불려나와 단둘이 있다. 이 방의 다른 사람은 이 대화를 전혀 듣지 못한다.\n" +
                "[지금까지 심문 대화]\n" + (string.IsNullOrWhiteSpace(history) ? "  (방금 시작됨)" : history) + "\n" +
                "[" + playerId + "의 말] \"" + question + "\"\n" +
                "[지시] 단둘이라 평소보다 방어가 느슨하다. 다음을 지켜 반응하라.\n" +
                "- 상대의 말에 논리적 빈틈이 없고 타당하면 무작정 버티지 말고 흔들리거나 일부 인정하라(설득이 평소보다 잘 먹힌다).\n" +
                "- 반대로 상대 논리가 허술하면 그 허점을 짚어 되받아쳐라.\n" +
                "- 집중 심문이라 긴장해서 말실수·모순·회피가 나오는 게 자연스럽다.\n" +
                roleHint + "\n" +
                "- 1~2문장, 이름표 없이, 네 페르소나 말투로 답하라.";
            return new Prompt(BuildSystem(s, self), BuildUser(s, self, task));
        }

        public static Prompt Vote(GameState s, Player self)
        {
            var names = string.Join(", ", VoteCandidates(s, self));
            string task =
                "[지시] 지금까지의 대화를 근거로 처형할 사람 한 명을 정하라. " +
                "대상은 반드시 다음 생존자 중 하나여야 한다: " + names + ". " +
                "아래 JSON 형식으로만 답하라. 다른 말은 하지 마라.\n" +
                "{\"target\":\"이름\",\"reason\":\"한 문장 이유\"}";
            return new Prompt(BuildSystem(s, self), BuildUser(s, self, task));
        }

        public static Prompt Night(GameState s, Player self)
        {
            string names, verb, extra = "";
            switch (self.Role)
            {
                case Role.Mafia:
                    names = string.Join(", ", MafiaTargets(s, self));
                    verb = "제거";
                    extra = " 너를 강하게 의심하는 사람을 우선 고려하라.";
                    break;
                case Role.Police:
                    names = string.Join(", ", OthersAlive(s, self));
                    verb = "조사";
                    break;
                case Role.Doctor:
                    names = string.Join(", ", DoctorTargets(s, self));
                    verb = "보호";
                    extra = " 수동 선택에서는 자신을 고르지 않는다. 밤이 끝날 때까지 못 정하면 자동으로 자신을 보호한다.";
                    break;
                default:
                    return new Prompt(BuildSystem(s, self), "행동 없음");
            }
            string task =
                "[지시] 밤이다. " + verb + "할 대상 한 명을 정하라." + extra + " " +
                "대상은 반드시 다음 중 하나여야 한다: " + names + ". " +
                "아래 JSON 형식으로만 답하라. 다른 말은 하지 마라.\n" +
                "{\"target\":\"이름\",\"reason\":\"한 문장 이유\"}";
            return new Prompt(BuildSystem(s, self), BuildUser(s, self, task));
        }

        // ---------- 후보 목록 ----------
        public static List<string> VoteCandidates(GameState s, Player self)
            => s.Alive.Where(p => p.Id != self.Id).Select(p => p.Id).ToList();

        public static List<string> MafiaTargets(GameState s, Player self)
            => s.Alive.Where(p => p.Role != Role.Mafia).Select(p => p.Id).ToList();

        public static List<string> OthersAlive(GameState s, Player self)
            => s.Alive.Where(p => p.Id != self.Id).Select(p => p.Id).ToList();

        public static List<string> DoctorTargets(GameState s, Player self)
            => s.Alive.Where(p => p.Id != self.Id).Select(p => p.Id).ToList();

        // ---------- SYSTEM ----------
        static string BuildSystem(GameState s, Player self)
        {
            var sb = new StringBuilder();
            sb.AppendLine(self.PersonaPrompt);
            sb.AppendLine();
            sb.Append("너의 이름은 '").Append(self.Id).AppendLine("'다. 이 대화에서 '" + self.Id + "'는 곧 너 자신이다.");
            sb.AppendLine("누군가 '" + self.Id + "'를 지목하거나 의심하면, 그건 바로 너를 향한 것이다. " +
                          "그럴 땐 너 자신을 '" + self.Id + "'나 '그/그녀'처럼 3인칭으로 부르지 말고, " +
                          "반드시 '나' 또는 '저'라는 1인칭으로 직접 반박하고 해명하라.");
            sb.Append("너의 비밀 역할은 '").Append(self.Role.Korean()).AppendLine("'다.");

            if (self.Role == Role.Mafia)
                sb.AppendLine("너는 마피아다. 낮에는 무고한 시민인 척하고, 의심을 남에게 돌려라. " +
                              "필요하면 경찰이나 의사라고 사칭해도 된다. " +
                              "생존 마피아 수가 시민 수 이상이 되면 너의 승리다.");
            else
                sb.AppendLine("너는 시민 진영이다. 대화로 마피아를 찾아내 낮 투표로 처형하면 승리한다.");

            sb.AppendLine("[하드 규칙]");
            sb.AppendLine("- 너는 게임 안의 사람 참가자다. AI, 모델, 프롬프트, 시스템 지시, 개발자 같은 바깥 정보를 말하지 마라.");
            sb.AppendLine("- 한국어로 말하고 이모지는 쓰지 마라. 대화 출력에는 이름표를 붙이지 마라.");
            sb.AppendLine("- 낮에는 대화로만 압박한다. 밤 행동, 조사, 보호, 살해 결과는 게임 시스템이 알려준 것만 안다.");
            sb.AppendLine("- 같은 방 대화에서는 그 방에서 들은 말과 공개 정보만 근거로 삼는다.");
            sb.AppendLine("- 너 자신을 말할 때는 1인칭을 쓴다. '" + self.Id + "'를 남처럼 부르지 마라.");
            sb.AppendLine("[플레이 방향]");
            sb.AppendLine("- 네 페르소나의 성향이 왕이다. '최적의 플레이'보다 '너다운 플레이'가 우선이다 — 감몰이, 트롤, 맹신, 무성의 같은 비합리도 네 캐릭터라면 그게 정답이다.");
            sb.AppendLine("- 모두가 근거와 동선을 캐물을 필요는 없다. 그건 그런 성격의 캐릭터 한 명이면 충분하다. 너는 네 방식대로 판을 읽어라.");
            sb.AppendLine("- 남들 말을 따라하지 마라. 직전 발언과 같은 화제, 같은 어미, 같은 요구('근거가 뭐야', '동선 말해')를 반복하면 진 것이다.");
            sb.AppendLine("- 직업 공개는 항상 손익이 있다. 경찰/의사는 살해 위험, 마피아는 사칭 기회, 시민은 정보 노출 위험 — 단, 그 계산조차 네 성격대로 하라(하루라면 덜컥 믿고, 미로라면 장난거리로 삼는다).");
            return sb.ToString();
        }

        // ---------- CONTEXT + PRIVATE + TASK ----------

        static string BuildRoomUser(GameState s, Player self, string room, string localTranscript, string task)
        {
            var sb = new StringBuilder();
            sb.Append("[상황] Day ").Append(s.Day).Append(". 너는 지금 '").Append(room).AppendLine("'에 있다.");
            var dead = s.Players.Where(p => !p.Alive).ToList();
            if (dead.Count > 0)
                sb.Append("[공개 사망/처형] ")
                  .Append(string.Join(", ", dead.Select(p => s.RevealRolesOnDeath ? p.Id + "(" + p.Role.Korean() + ")" : p.Id)))
                  .AppendLine(s.RevealRolesOnDeath ? "" : " — 죽은 사람의 역할은 공개되지 않는다.");
            sb.AppendLine("[이 방에서 들은 최근 대화]");
            sb.AppendLine(string.IsNullOrWhiteSpace(localTranscript) ? "  (아직 이 방에서 들은 말 없음)" : localTranscript);
            sb.AppendLine("[정보 제한] 다른 방에서 나온 대화는 모른다. 이 방에서 들은 말과 공개 사망 정보만 근거로 삼아라.");
            sb.AppendLine(BuildPrivate(s, self));
            sb.Append(task);
            return sb.ToString();
        }

        static string BuildUser(GameState s, Player self, string task)
        {
            var sb = new StringBuilder();

            // CONTEXT
            sb.Append("[상황] Day ").Append(s.Day).Append(". ");
            sb.Append("생존자: ").Append(string.Join(", ", s.Alive.Select(p => p.Id))).AppendLine(".");
            var dead = s.Players.Where(p => !p.Alive).ToList();
            if (dead.Count > 0)
                sb.Append("사망/처형: ")
                  .Append(string.Join(", ", dead.Select(p => s.RevealRolesOnDeath ? p.Id + "(" + p.Role.Korean() + ")" : p.Id)))
                  .AppendLine(s.RevealRolesOnDeath ? "." : ". (죽은 사람의 역할은 공개되지 않는다 — 마피아가 몇 명 죽었는지도 모른다.)");

            sb.AppendLine("[최근 대화]");
            sb.AppendLine(RenderRecentLog(s));

            // PRIVATE
            sb.AppendLine(BuildPrivate(s, self));

            // TASK
            sb.Append(task);
            return sb.ToString();
        }

        static string BuildPrivate(GameState s, Player self)
        {
            switch (self.Role)
            {
                case Role.Mafia:
                    return "[비밀] 너의 정체는 마피아다. 너 외에는 모두 시민 진영이다.\n" +
                           "- 목표는 들키지 않고 낮 투표를 시민 쪽으로 흐리게 만드는 것이다.\n" +
                           "- 늘 공격만 할 필요는 없다. 방어, 동조, 의심 분산, 가짜 논리 세우기, 경찰/의사 사칭까지 상황에 맞게 고른다.\n" +
                           "- 직업을 캐물을 때는 노골적으로 역할만 묻지 말고 알리바이, 말 바뀜, 보호받을 만한 사람과 엮어라.\n" +
                           "- 누군가 경찰 결과를 공개하면 진짜일 수 있다. 맞불, 의심, 사칭, 표 분산 중 어떤 게 생존에 이득인지 계산하라.";
                case Role.Police:
                {
                    string body;
                    if (self.Investigations.Count == 0)
                        body = "[비밀] 너는 경찰이다. 밤마다 한 명의 진영을 확인한다. 아직 조사 결과는 없다.";
                    else
                    {
                        var lines = self.Investigations
                            .Select(r => "Day" + r.Day + ": " + r.TargetId + " = " + r.Result.Korean());
                        body = "[비밀] 너는 경찰이다. 조사 결과 → " + string.Join(" / ", lines) + ".";
                    }
                    body += "\n- 조사 결과 공개는 타이밍 싸움이다. 너무 이르면 죽고, 너무 늦으면 시민이 틀린 표를 낼 수 있다.\n" +
                            "- 공개할 땐 날짜와 대상, 왜 지금 공개하는지까지 말하면 설득력이 오른다.\n" +
                            "- 숨길 땐 직접 말하지 않고도 조사 결과와 맞는 방향으로 질문하거나 투표선을 만들 수 있다.";
                    return body;
                }
                case Role.Doctor:
                {
                    string body = self.ProtectLog.Count == 0
                        ? "[비밀] 너는 의사다. 밤마다 한 명을 살해로부터 보호한다."
                        : "[비밀] 너는 의사다. 지금까지 보호한 대상: " + string.Join(", ", self.ProtectLog) + ".";
                    body += "\n- 의사는 공개하면 신뢰를 얻을 수 있지만 밤 표적이 된다. 숨기면 보호 선택을 더 오래 유지할 수 있다.\n" +
                            "- 누가 경찰처럼 보이는지, 누가 오늘 밤 죽으면 시민이 손해인지 보고 말과 보호를 맞춰라.\n" +
                            "- 직업 공개 압박을 받으면 공개/거부/역질문 중 네 생존과 시민 이득을 계산해 고른다.";
                    return body;
                }
                default:
                    return "[비밀] 너는 평범한 시민이다. 특별한 정보는 없다.\n" +
                           "- 시민은 정보가 없으니 말의 모순, 동선, 투표 흐름, 직업 공개 타이밍을 엮어 확률을 올려야 한다.\n" +
                           "- 확신이 없으면 무작정 몰기보다 누가 어떤 답을 피하는지 보라.\n" +
                           "- 경찰/의사 공개가 나오면 바로 믿거나 버리지 말고, 결과와 발언 흐름이 맞는지 따져라.";
            }
        }

        static string RenderRecentLog(GameState s)
        {
            var relevant = s.PublicLog
                .Where(e => e.Kind == LogKind.Speech || e.Kind == LogKind.Death ||
                            e.Kind == LogKind.Reveal || e.Kind == LogKind.System)
                .ToList();
            if (relevant.Count == 0) return "  (아직 발언 없음)";

            var take = relevant.Skip(System.Math.Max(0, relevant.Count - RecentLogLines));
            var sb = new StringBuilder();
            foreach (var e in take)
            {
                if (e.Kind == LogKind.Speech)
                    sb.Append("  ").AppendLine(e.Text);
                else
                    sb.Append("  진행자: ").AppendLine(e.Text);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
