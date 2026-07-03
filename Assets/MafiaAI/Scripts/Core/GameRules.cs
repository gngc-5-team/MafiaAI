using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Core
{
    /// <summary>테스트 가능하도록 난수를 추상화.</summary>
    public interface IRng
    {
        int Next(int maxExclusive);
    }

    public sealed class SystemRng : IRng
    {
        readonly System.Random _r;
        public SystemRng(int? seed = null)
            => _r = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        public int Next(int maxExclusive) => _r.Next(maxExclusive);
    }

    public struct NightResult
    {
        public string KilledId;        // 사망자 id, 없으면 null
        public bool Protected;         // 의사가 마피아 대상을 막았는가
        public bool HasPoliceResult;
        public string PoliceTargetId;
        public Faction PoliceResult;
    }

    public struct VoteResult
    {
        public string ExecutedId;              // 처형자 id, 없으면 null
        public bool Tie;                       // 최다 득표 동수였는가
        public List<string> TopTied;           // 최다 득표 동수 후보들
        public Dictionary<string, int> Tally;  // 대상 id -> 득표수
    }

    public static class GameRules
    {
        /// <summary>6인 표준 구성: 마피아1, 경찰1, 의사1, 시민3.</summary>
        public static readonly Role[] StandardRoles =
        {
            Role.Mafia, Role.Police, Role.Doctor,
            Role.Citizen, Role.Citizen, Role.Citizen
        };

        /// <summary>역할을 셔플해 플레이어들에게 배정.</summary>
        public static void AssignRoles(List<Player> players, IRng rng)
        {
            var roles = new List<Role>(StandardRoles);
            // Fisher-Yates
            for (int i = roles.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (roles[i], roles[j]) = (roles[j], roles[i]);
            }
            for (int i = 0; i < players.Count; i++)
                players[i].Role = roles[i];
        }

        /// <summary>밤 액션 정산: 마피아 살해를 의사 보호가 막을 수 있고, 경찰 조사 결과를 산출.</summary>
        public static NightResult ResolveNight(GameState s)
        {
            var result = new NightResult();
            var n = s.Night;

            // 경찰 조사
            if (!string.IsNullOrEmpty(n.PoliceTarget))
            {
                var target = s.ById(n.PoliceTarget);
                var police = s.OfRole(Role.Police);
                if (target != null && police != null)
                {
                    var faction = target.Faction;
                    result.HasPoliceResult = true;
                    result.PoliceTargetId = target.Id;
                    result.PoliceResult = faction;
                    police.Investigations.Add(new InvestigationResult
                    {
                        Day = s.Day,
                        TargetId = target.Id,
                        Result = faction
                    });
                }
            }

            // 의사 보호 기록
            if (!string.IsNullOrEmpty(n.DoctorTarget))
            {
                var doc = s.OfRole(Role.Doctor);
                doc?.ProtectLog.Add(n.DoctorTarget);
            }

            // 마피아 살해 (의사 보호 시 무효)
            if (!string.IsNullOrEmpty(n.MafiaTarget))
            {
                bool saved = n.MafiaTarget == n.DoctorTarget;
                if (saved)
                {
                    result.Protected = true;
                }
                else
                {
                    var victim = s.ById(n.MafiaTarget);
                    if (victim != null && victim.Alive)
                    {
                        victim.Alive = false;
                        result.KilledId = victim.Id;
                    }
                }
            }

            return result;
        }

        /// <summary>투표 집계. 최다 득표 동수면 rng로 한 명을 무작위 선택해 처형.</summary>
        public static VoteResult ResolveVotes(GameState s, IRng rng)
        {
            var tally = new Dictionary<string, int>();
            foreach (var kv in s.Votes)
            {
                var target = kv.Value;
                if (string.IsNullOrEmpty(target)) continue; // 기권
                tally.TryGetValue(target, out int c);
                tally[target] = c + 1;
            }

            var result = new VoteResult { Tally = tally, TopTied = new List<string>() };

            if (tally.Count == 0)
            {
                result.ExecutedId = null;
                return result;
            }

            int max = tally.Values.Max();
            result.TopTied = tally.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
            result.Tie = result.TopTied.Count > 1;

            string chosen = result.TopTied[rng.Next(result.TopTied.Count)];
            result.ExecutedId = chosen;

            var executed = s.ById(chosen);
            if (executed != null) executed.Alive = false;

            return result;
        }

        /// <summary>승패 판정. 마피아 전멸=시민 승, 마피아≥시민=마피아 승.</summary>
        public static Winner CheckWinner(GameState s)
        {
            int mafia = s.Alive.Count(p => p.Role == Role.Mafia);
            int citizens = s.Alive.Count(p => p.Role != Role.Mafia);

            if (mafia == 0) return Winner.Citizens;
            if (mafia >= citizens) return Winner.Mafia;
            return Winner.None;
        }
    }
}
