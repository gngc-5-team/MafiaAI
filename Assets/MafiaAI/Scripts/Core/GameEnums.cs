namespace MafiaAI.Core
{
    /// <summary>비밀 역할.</summary>
    public enum Role
    {
        Citizen,
        Mafia,
        Police,
        Doctor
    }

    /// <summary>진영.</summary>
    public enum Faction
    {
        Citizen,
        Mafia
    }

    /// <summary>하루 사이클의 페이즈.</summary>
    public enum Phase
    {
        Night,
        Dawn,
        Discuss,
        Vote,
        End
    }

    /// <summary>승리 진영.</summary>
    public enum Winner
    {
        None,
        Citizens,
        Mafia
    }

    /// <summary>공개 로그 한 줄의 종류.</summary>
    public enum LogKind
    {
        System,
        Speech,
        Death,
        Vote,
        Reveal
    }

    public static class RoleExtensions
    {
        public static Faction GetFaction(this Role r)
            => r == Role.Mafia ? Faction.Mafia : Faction.Citizen;

        public static string Korean(this Role r) => r switch
        {
            Role.Mafia => "마피아",
            Role.Police => "경찰",
            Role.Doctor => "의사",
            _ => "시민"
        };

        public static string Korean(this Faction f)
            => f == Faction.Mafia ? "마피아" : "시민";
    }
}
