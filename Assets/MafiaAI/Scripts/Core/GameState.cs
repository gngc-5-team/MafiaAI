using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Core
{
    /// <summary>공개 대화/시스템 로그 한 줄.</summary>
    public struct LogEntry
    {
        public int Day;
        public Phase Phase;
        public LogKind Kind;
        public string Speaker;   // 인물 id 또는 "SYSTEM"
        public string Text;

        public LogEntry(int day, Phase phase, LogKind kind, string speaker, string text)
        {
            Day = day;
            Phase = phase;
            Kind = kind;
            Speaker = speaker;
            Text = text;
        }
    }

    /// <summary>이번 밤에 지정된 능력 대상(매 밤 리셋).</summary>
    public class NightActions
    {
        public string MafiaTarget;
        public string DoctorTarget;
        public string PoliceTarget;

        public void Reset()
        {
            MafiaTarget = null;
            DoctorTarget = null;
            PoliceTarget = null;
        }
    }

    /// <summary>게임 전체 상태.</summary>
    public class GameState
    {
        public int Day;
        public Phase Phase = Phase.Night;
        public Winner Winner = Winner.None;

        public readonly List<Player> Players = new();
        public readonly List<LogEntry> PublicLog = new();
        public readonly NightActions Night = new();

        // 투표: voterId -> targetId (매 투표 리셋)
        public readonly Dictionary<string, string> Votes = new();

        public IEnumerable<Player> Alive => Players.Where(p => p.Alive);
        public List<Player> AliveList => Players.Where(p => p.Alive).ToList();

        public Player ById(string id) => Players.FirstOrDefault(p => p.Id == id);
        public Player Human => Players.FirstOrDefault(p => p.IsHuman);
        public Player Mafia => Players.FirstOrDefault(p => p.Role == Role.Mafia);
        public Player OfRole(Role r) => Players.FirstOrDefault(p => p.Role == r && p.Alive);

        public void Log(LogKind kind, string speaker, string text)
            => PublicLog.Add(new LogEntry(Day, Phase, kind, speaker, text));
    }
}
