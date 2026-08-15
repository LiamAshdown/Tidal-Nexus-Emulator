using System.Collections.Generic;
using System.Text.RegularExpressions;
using TidalNexus.StandaloneServer.Data;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class AchievementService
    {

        public const string NpcKills = "npc.kills";
        public const string BossKills = "boss.kills";
        public const string PvpKills = "pvp.kills";
        public const string Capsules = "capsules";
        public const string TradeUnits = "trade.units";
        public const string TradeCredits = "trade.credits";
        public const string MissionsDone = "missions.done";
        public const string TorpedoHits = "hits.torpedo";
        public const string MineHits = "hits.mine";
        public const string BombHits = "hits.bomb";
        public const string Distance = "distance";

        private const string Level = "@level";
        private const string Fame = "@fame";
        private const string Earned = "@achievements";

        private sealed class Goal
        {
            public AchievementData Achievement;
            public int Tier;
            public string Key;
            public long Amount;
        }

        private List<Goal> _goals;
        private int _dormant;

        private static readonly (string Pattern, string Key)[] Patterns =
        {

            (@"^Kill \{0\} bosses", BossKills),
            (@"^Kill \{0\} NPC units", NpcKills),
            (@"^Get \{0\} PvP kills", PvpKills),
            (@"^Get a PvP kill", PvpKills),
            (@"^Reach level \{0\}", Level),
            (@"^Gain \{0\} total Fame", Fame),
            (@"^Complete \{0\} Achievements", Earned),
            (@"^Collect \{0\} Capsules$", Capsules),
            (@"^Sell \{0\} (trade goods|materials or trade goods|collected materials)", TradeUnits),
            (@"^Earn \{0\} Credits by trading", TradeCredits),
            (@"^Complete \{0\} (tasks|daily missions|PvE tasks|PvP tasks|trading tasks)", MissionsDone),
            (@"^Land \{0\} torpedo hits", TorpedoHits),
            (@"^Land \{0\} mine hits", MineHits),
            (@"^Land \{0\} bomb hits", BombHits),
            (@"^Travel \{0\} units", Distance),
        };

        private List<Goal> Goals
        {
            get
            {
                if (_goals != null)
                {
                    return _goals;
                }

                _goals = new List<Goal>();
                _dormant = 0;

                List<AchievementData> catalogue = GameData.Rates?.achievements;
                if (catalogue == null)
                {
                    ServerLog.Warn("ServerRateSettings has no achievements");
                    return _goals;
                }

                var seen = new HashSet<int>();

                foreach (AchievementData achievement in catalogue)
                {

                    if (achievement?.tiers == null || !seen.Add(achievement.index))
                    {
                        continue;
                    }

                    for (int tier = 0; tier < achievement.tiers.Count; tier++)
                    {
                        AchievementData.AchievementTier t = achievement.tiers[tier];
                        string key = KeyFor(t?.description);

                        if (key == null)
                        {
                            _dormant++;
                            continue;
                        }

                        _goals.Add(new Goal
                        {
                            Achievement = achievement,
                            Tier = tier,
                            Key = key,
                            Amount = t.amount,
                        });
                    }
                }

                ServerLog.Info($"achievements: {seen.Count} loaded, {_goals.Count} tiers "
                    + $"tracked, {_dormant} waiting on systems not yet built");

                return _goals;
            }
        }

        private static string KeyFor(string description)
        {
            if (string.IsNullOrWhiteSpace(description) || description == "None")
            {
                return null;
            }

            Match boss = Regex.Match(description,
                @"^Manage to defeat \[?BOSS\]?\s*([A-Za-z]+)", RegexOptions.IgnoreCase);

            if (boss.Success)
            {
                return BossKills + "." + boss.Groups[1].Value;
            }

            foreach ((string pattern, string key) in Patterns)
            {
                if (Regex.IsMatch(description, pattern, RegexOptions.IgnoreCase))
                {
                    return key;
                }
            }

            return null;
        }

        public static string BossKeyFor(string npcName)
        {
            if (string.IsNullOrEmpty(npcName))
            {
                return BossKills;
            }

            Match m = Regex.Match(npcName, @"BOSS[\]_]*\s*([A-Za-z]+)",
                RegexOptions.IgnoreCase);

            return m.Success ? BossKills + "." + m.Groups[1].Value : BossKills;
        }

        public void Bump(Account account, string key, long amount = 1)
        {
            if (account == null || amount <= 0)
            {
                return;
            }

            account.AddStat(key, amount);
            Evaluate(account);
        }

        public bool Award(Account account, AchievementData achievement, int tier)
        {
            if (account == null || achievement?.tiers == null ||
                tier < 0 || tier >= achievement.tiers.Count)
            {
                return false;
            }

            string token = achievement.index + ":" + tier;
            if (account.achievements.Contains(token))
            {
                return false;
            }

            account.achievements.Add(token);

            List<Unlockable> rewards = achievement.tiers[tier].rewards;
            if (rewards != null)
            {
                foreach (Unlockable unlockable in rewards)
                {
                    ServerHub.Progression?.GrantUnlockable(account, unlockable);
                }
            }

            AccountStore.MarkDirty(account);
            ServerLog.Info($"{account.nickname} earned achievement "
                + $"\"{achievement.tiers[tier].name}\"");
            return true;
        }

        public List<string> Evaluate(Account account)
        {
            var earned = new List<string>();
            if (account == null)
            {
                return earned;
            }

            foreach (Goal goal in Goals)
            {
                if (Progress(account, goal.Key) >= goal.Amount &&
                    Award(account, goal.Achievement, goal.Tier))
                {
                    earned.Add(goal.Achievement.index + ":" + goal.Tier);
                }
            }

            if (earned.Count == 0)
            {
                return earned;
            }

            foreach (Goal goal in Goals)
            {
                if (goal.Key == Earned &&
                    Progress(account, Earned) >= goal.Amount &&
                    Award(account, goal.Achievement, goal.Tier))
                {
                    earned.Add(goal.Achievement.index + ":" + goal.Tier);
                }
            }

            return earned;
        }

        public long ProgressOf(Account account, int achievementIndex)
        {
            foreach (Goal goal in Goals)
            {
                if (goal.Achievement.index == achievementIndex)
                {
                    return Progress(account, goal.Key);
                }
            }

            return 0;
        }

        public IEnumerable<AchievementData> All
        {
            get
            {
                var seen = new HashSet<int>();
                List<AchievementData> catalogue = GameData.Rates?.achievements;

                if (catalogue == null)
                {
                    yield break;
                }

                foreach (AchievementData achievement in catalogue)
                {
                    if (achievement != null && seen.Add(achievement.index))
                    {
                        yield return achievement;
                    }
                }
            }
        }

        public int EarnedTiersIn(Account account, AchievementData.AchievementCategory category)
        {
            int earned = 0;

            foreach (AchievementData achievement in All)
            {
                if (achievement.category != category || achievement.tiers == null)
                {
                    continue;
                }

                for (int tier = 0; tier < achievement.tiers.Count; tier++)
                {
                    if (account.achievements.Contains(achievement.index + ":" + tier))
                    {
                        earned++;
                    }
                }
            }

            return earned;
        }

        public static AchievementData.AchievementCategory? Summarises(
            AchievementData.AchievementCategory category)
        {
            int value = (int)category;
            return value >= 8 && value <= 15
                ? (AchievementData.AchievementCategory)(value - 8)
                : (AchievementData.AchievementCategory?)null;
        }

        public string RecentTokens(Account account, int max = 10)
        {
            if (account == null)
            {
                return string.Empty;
            }

            var recent = new List<string>();

            for (int i = account.achievements.Count - 1; i >= 0 && recent.Count < max; i--)
            {
                if (Resolve(account.achievements[i], out AchievementData data, out int tier))
                {
                    recent.Add(data.index + "_" + tier);
                }
            }

            return string.Join(",", recent);
        }

        public int PointsOf(Account account)
        {
            if (account == null)
            {
                return 0;
            }

            int points = 0;

            foreach (string token in account.achievements)
            {
                if (Resolve(token, out AchievementData data, out int tier))
                {
                    points += data.tiers[tier].points;
                }
            }

            return points;
        }

        private Dictionary<int, AchievementData> _byIndex;

        private Dictionary<int, AchievementData> ByIndex
        {
            get
            {
                if (_byIndex != null)
                {
                    return _byIndex;
                }

                _byIndex = new Dictionary<int, AchievementData>();
                foreach (AchievementData achievement in All)
                {
                    _byIndex[achievement.index] = achievement;
                }

                return _byIndex;
            }
        }

        private bool Resolve(string token, out AchievementData achievement, out int tier)
        {
            achievement = null;
            tier = 0;

            int split = token != null ? token.IndexOf(':') : -1;

            if (split <= 0 ||
                !int.TryParse(token.Substring(0, split), out int index) ||
                !int.TryParse(token.Substring(split + 1), out tier))
            {
                return false;
            }

            return ByIndex.TryGetValue(index, out achievement) &&
                   achievement.tiers != null &&
                   tier >= 0 && tier < achievement.tiers.Count;
        }

        private static long Progress(Account account, string key)
        {
            switch (key)
            {
                case Level: return account.level;
                case Fame: return account.lifetimeFame;

                case Earned:
                    int total = 0;
                    foreach (string token in account.achievements)
                    {
                        if (token.Contains(":"))
                        {
                            total++;
                        }
                    }

                    return total;

                default: return account.Stat(key);
            }
        }
    }
}
