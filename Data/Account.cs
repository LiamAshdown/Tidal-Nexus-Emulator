using System;
using System.Collections.Generic;

namespace TidalNexus.StandaloneServer.Data
{

    [Serializable]
    public sealed class Account
    {

        public string id = string.Empty;
        public string nickname = string.Empty;

        public int faction = 3;
        public string clanTag = string.Empty;

        public int title = -1;
        public int design = -1;

        public int skin = -1;
        public int sentryDesign = -1;
        public int hpxDesign = -1;
        public int spxDesign = -1;
        public bool admin;
        public bool banned;

        public long bannedUntilUnix;

        public long mutedUntilUnix;

        public const long PermanentBan = 4102444800L;

        public long BanExpiryUnix => bannedUntilUnix > 0 ? bannedUntilUnix : PermanentBan;

        public int permission;

        public int Role
        {
            get => admin && permission < 10 ? 10 : permission;
            set
            {
                permission = value;
                admin = value >= 10;
            }
        }

        public const int MaxBorax = 1_000_000_000;

        public const int NeverPlacedArena = 9999;

        public int level = 1;
        public long experience;
        public int prestige;
        public long credits = 10000;
        public long premium = 1000;
        public int borax;
        public int honour;
        public int battlePoints;

        public int hull = 4300;
        public int hullMax = 4300;
        public int shield = 4300;
        public int shieldMax = 4300;
        public int cargoMax = 650;
        public int hpx;
        public int spx;
        public int sentry;

        public int turbineIndex = 1;

        public long weaponSlots = 1000000000L;
        public long hullSlots = 1000000000L;
        public long shieldSlots = 1000000000L;
        public long turbineSlots = 1000000000L;

        public static int FittedInSlotZero(long slots)
        {
            string digits = slots.ToString();
            return digits.Length > 0 ? digits[0] - '0' : 0;
        }

        public int energy = 100;

        public int ammoIndex;

        public int ammo1 = 5000;
        public int ammo2;
        public int ammo3;
        public int ammo4;
        public int ammo5;
        public int photonAmmo1;
        public int photonAmmo2;
        public int photonAmmo3;
        public int photonAmmo4;
        public int torpedoIndex;
        public int bombIndex;
        public int mineIndex;
        public int decoyIndex;

        public int torpedo1;
        public int torpedo2;
        public int torpedo3;
        public int torpedo4;
        public int torpedoice;
        public int photonTorpedo1;
        public int photonTorpedo2;
        public int photonTorpedo3;
        public int photonTorpedo4;
        public int bomb1;
        public int bomb2;
        public int bomb3;
        public int photonBomb1;
        public int photonBomb2;
        public int photonBomb3;
        public int mine1;
        public int mine2;
        public int mine3;
        public int photonMine1;
        public int photonMine2;
        public int photonMine3;
        public int decoy1;
        public int decoy2;
        public int decoy3;

        public long lifetimeKills;
        public long weeklyKills;
        public long lifetimeFame;
        public long weeklyFame;
        public long lifetimeArena;
        public long weeklyArena;
        public long deaths;

        public int lastArenaPos;

        public long pvpFlaggedUntilUnix;

        public long vipUntilUnix;
        public bool founderPack;
        public long lastVipRewardUnix;

        public List<InventoryStack> cargo = new List<InventoryStack>();

        public List<InventoryStack> inventory = new List<InventoryStack>();

        public List<int> extras = new List<int>();
        public List<int> equippedExtras = new List<int>();

        public List<int> activeExtras = new List<int>();
        public List<int> titles = new List<int>();
        public List<int> designs = new List<int>();

        public List<int> skins = new List<int>();
        public List<int> sentryDesigns = new List<int>();
        public List<int> hpxDesigns = new List<int>();
        public List<int> spxDesigns = new List<int>();

        public List<string> achievements = new List<string>();

        public List<StatCounter> stats = new List<StatCounter>();

        public long Stat(string key)
        {
            foreach (StatCounter s in stats)
            {
                if (s.key == key)
                {
                    return s.value;
                }
            }

            return 0;
        }

        public long AddStat(string key, long amount)
        {
            foreach (StatCounter s in stats)
            {
                if (s.key == key)
                {
                    s.value += amount;
                    return s.value;
                }
            }

            stats.Add(new StatCounter { key = key, value = amount });
            return amount;
        }
        public List<ActiveMission> missions = new List<ActiveMission>();

        public float x;
        public float y = 5f;
        public float z;

        public long lastSeenUnix;

        public bool IsVip => vipUntilUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public bool IsMuted => mutedUntilUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public int CargoUsed
        {
            get
            {
                int total = 0;
                foreach (InventoryStack s in cargo)
                {
                    total += s.count;
                }

                return total;
            }
        }
    }

    [Serializable]
    public sealed class StatCounter
    {
        public string key = string.Empty;
        public long value;
    }

    [Serializable]
    public sealed class InventoryStack
    {
        public string itemId = string.Empty;
        public int count;

        public InventoryStack()
        {
        }

        public InventoryStack(string itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }

    [Serializable]
    public sealed class ActiveMission
    {
        public int templateId;

        public int[] progress = new int[1];

        public int[] target = new int[1];
        public long acceptedUnix;
        public bool complete;
    }

    [Serializable]
    public sealed class Clan
    {
        public string tag = string.Empty;
        public string name = string.Empty;
        public string description = string.Empty;
        public string banner = "0,0,0,0,0,0,0,0";
        public string founderId = string.Empty;
        public int faction;
        public long createdUnix;
        public long lifetimeFame;
        public long weeklyFame;
        public long lifetimeKills;
        public long weeklyKills;
        public List<ClanMember> members = new List<ClanMember>();
        public List<string> invited = new List<string>();
        public List<string> wars = new List<string>();

        public bool factionChangeVote;
        public List<string> factionVoters = new List<string>();
    }

    [Serializable]
    public sealed class ClanMember
    {
        public string accountId = string.Empty;
        public string nickname = string.Empty;
        public int rank;
        public long joinedUnix;
    }
}
