using System.Collections.Generic;
using System.Linq;

namespace MafiaAI.Alibi
{
    /// <summary>한 사람이 한 시간대에 대해 하는 진술. 방 + 그 방에서 본 사람.</summary>
    public sealed class Claim
    {
        public string Room;
        public List<string> Seen; // 빈 리스트 = "아무도 못 봤다"

        public Claim(string room, IEnumerable<string> seen)
        {
            Room = room;
            Seen = seen != null ? seen.ToList() : new List<string>();
        }

        public Claim Clone() => new Claim(Room, Seen);

        public bool SameAs(Claim o) =>
            o != null && Room == o.Room && Seen.Count == o.Seen.Count && !Seen.Except(o.Seen).Any();
    }

    /// <summary>거짓말이 어떤 사실과 부딪히도록 설계됐는가(생성기가 고른 틀).</summary>
    public enum LieKind
    {
        None,
        Presence, // 다른 사람이 실제로 있던 방을 댔다 → 그 사람은 못 봤다고 한다
        Travel,   // 목격된 위치에서 15분 안에 갈 수 없는 방을 댔다
        Witness,  // 누군가 실제로 봤는데 다른 방에 있었다고 우긴다
    }

    /// <summary>용의자 한 명. 실제 동선(진실)과 현재 진술(거짓 포함)을 따로 들고 있다.</summary>
    public sealed class Suspect
    {
        public string Name;
        public string[] TruePath;        // 슬롯별 실제 위치
        public List<string>[] TrueSeen;  // 슬롯별 실제로 마주친 사람(피해자 포함)
        public Claim[] Claims;           // 슬롯별 현재 진술(비밀 고백 시 진실로 갱신)
        public Claim[] OriginalClaims;   // 처음 진술(결과 화면·보드 정정 표시용)

        public bool IsKiller;
        public bool IsSecretLiar;
        public string SecretRoom;        // 레드헤링이 실제로 있던 비밀의 방
        public int SecretSlot = -1;
        public string Secret;            // 숨긴 일(살인과 무관)
        public string Relation;          // 피해자와의 관계(잡담 재료)
        public bool Confessed;
        public bool Dead;                // 두 번째 살인의 희생자 — 더는 심문할 수 없다

        public Claim Truth(int slot) => new Claim(TruePath[slot], TrueSeen[slot]);
        public bool LiesAt(int slot) => !Claims[slot].SameAs(Truth(slot));
    }

    /// <summary>생성된 사건 하나. 엔진이 모든 진실을 알고, 용의자는 자기 몫만 안다.</summary>
    public sealed class AlibiCase
    {
        public static readonly string[] SlotTimes = { "22:45", "23:00", "23:15", "23:30" };
        public const int MurderSlot = 2;              // 23:15 비명
        public const string BodyFoundTime = "23:45";  // 하인이 시체를 발견한 시각(진술 슬롯 밖)

        public int Seed;
        public string Victim;
        public string[] VictimPath;     // 살해 시각까지의 피해자 동선(이후는 null)
        public string MurderRoom;
        public string Weapon;
        public string WeaponRoom;
        public int WeaponSlot;          // 범인이 흉기를 챙긴 시각(진실)
        public List<Suspect> Suspects = new List<Suspect>();
        public LieKind KillerLie;
        public LieKind SecretLie;
        public bool ChainOnly;          // 범인 모순이 비밀 고백 뒤에야 드러나는 사건

        public int SlotCount => SlotTimes.Length;
        public Suspect Get(string name) => Suspects.FirstOrDefault(s => s.Name == name);
        public Suspect Killer => Suspects.First(s => s.IsKiller);
        public Suspect SecretLiar => Suspects.First(s => s.IsSecretLiar);
        public int IndexOf(string name) => Suspects.FindIndex(s => s.Name == name);

        public static string TimeLabel(int slot) => SlotTimes[slot];

        /// <summary>"23:15" → "23시 15분"(대사용 표기 — 작은 모델이 숫자 표기를 따라 하게 한다).</summary>
        public static string SpokenTime(int slot)
        {
            var t = SlotTimes[slot].Split(':');
            return t[1] == "00" ? t[0] + "시" : t[0] + "시 " + t[1] + "분";
        }
    }

    /// <summary>보드의 한 칸(용의자 × 시간대)을 가리키는 좌표.</summary>
    public struct CellRef
    {
        public string Person;
        public int Slot;
        public CellRef(string person, int slot) { Person = person; Slot = slot; }
        public override string ToString() => Person + "@" + AlibiCase.TimeLabel(Slot);
    }
}
