using System;
using System.Collections.Generic;
using System.Linq;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public static class MissionCatalogue
    {
        private static MissionDataManager _manager;
        private static bool _searched;

        private static List<MissionData> _all;
        private static Dictionary<int, MissionData> _byIndex;

        public static MissionDataManager Manager
        {
            get
            {
                if (_manager != null || _searched)
                {
                    return _manager;
                }

                _searched = true;

                MissionDataManager[] found =
                    Resources.FindObjectsOfTypeAll<MissionDataManager>();

                if (found != null && found.Length > 0)
                {
                    _manager = found[0];
                }

                return _manager;
            }
        }

        public static List<MissionData> All
        {
            get
            {
                if (_all != null)
                {
                    return _all;
                }

                _all = new List<MissionData>();
                _byIndex = new Dictionary<int, MissionData>();

                List<MissionData> source = GameData.Rates?.missions;
                if (source == null)
                {
                    ServerLog.Warn(
                        "ServerRateSettings has no missions - the mission window will be empty");
                    return _all;
                }

                foreach (MissionData mission in source)
                {

                    if (mission == null || _byIndex.ContainsKey(mission.index))
                    {
                        continue;
                    }

                    _all.Add(mission);
                    _byIndex[mission.index] = mission;
                }

                ServerLog.Info($"mission catalogue: {_all.Count} missions, "
                    + $"{_all.Count(m => m.missionType == MissionType.PvpTask)} pvp, "
                    + $"{_all.Count(TradeShaped)} trade-shaped, "
                    + $"levels {_all.Min(m => m.missionLevel)}..{_all.Max(m => m.missionLevel)}");

                return _all;
            }
        }

        public static MissionData ById(int index)
        {
            if (_byIndex == null)
            {
                _ = All;
            }

            return _byIndex != null && _byIndex.TryGetValue(index, out MissionData mission)
                ? mission
                : null;
        }

        public static List<MissionData> Available(Account account, int max)
        {
            int level = account?.level ?? 1;

            List<MissionData> unlocked = All
                .Where(m => IsUnlocked(m, account) && !IsCompleted(m, account))
                .OrderBy(m => Math.Abs(level - m.missionLevel))
                .ThenBy(m => m.index)
                .ToList();

            if (unlocked.Count > max)
            {
                ServerLog.Info($"{unlocked.Count} missions unlocked at level {level}, "
                    + $"listing the {max} nearest");
                unlocked.RemoveRange(max, unlocked.Count - max);
            }

            return unlocked;
        }

        public static List<MissionData> PveTasks(Account account, int count) =>
            Pick(account, count, m => m.missionType != MissionType.PvpTask && !TradeShaped(m));

        public static List<MissionData> PvpTasks(Account account, int count) =>
            Pick(account, count, m => m.missionType == MissionType.PvpTask);

        public static List<MissionData> TradeTasks(Account account, int count) =>
            Pick(account, count, TradeShaped);

        private static bool TradeShaped(MissionData mission)
        {
            if (mission?.objectives == null)
            {
                return false;
            }

            foreach (MissionObjective objective in mission.objectives)
            {
                if (objective != null &&
                    (objective.type == MissionObjectiveType.MaterialBuy ||
                     objective.type == MissionObjectiveType.MaterialSell))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<MissionData> Pick(Account account, int count,
            Func<MissionData, bool> matches)
        {
            var result = new List<MissionData>();
            int level = account?.level ?? 1;

            var weighted = new List<(MissionData Mission, float Weight)>();

            foreach (MissionData mission in All)
            {
                if (!matches(mission) || !IsUnlocked(mission, account) ||
                    IsCompleted(mission, account))
                {
                    continue;
                }

                float weight = Manager != null
                    ? Manager.GetLevelWeight(level - mission.missionLevel)
                    : 1f;

                if (weight > 0f)
                {
                    weighted.Add((mission, weight));
                }
            }

            for (int i = 0; i < count && weighted.Count > 0; i++)
            {
                float total = weighted.Sum(w => w.Weight);
                float roll = UnityEngine.Random.value * total;
                float running = 0f;
                int chosen = weighted.Count - 1;

                for (int j = 0; j < weighted.Count; j++)
                {
                    running += weighted[j].Weight;
                    if (roll <= running)
                    {
                        chosen = j;
                        break;
                    }
                }

                result.Add(weighted[chosen].Mission);
                weighted.RemoveAt(chosen);
            }

            return result;
        }

        public static string CompletionToken(int missionIndex)
        {
            return "mission_" + missionIndex;
        }

        public static bool IsCompleted(MissionData mission, Account account)
        {
            return mission != null && account?.achievements != null &&
                   account.achievements.Contains(CompletionToken(mission.index));
        }

        public static bool IsUnlocked(MissionData mission, Account account)
        {
            if (mission == null)
            {
                return false;
            }

            if (account == null || mission.conditions == null || mission.conditions.Count == 0)
            {
                return true;
            }

            if (mission.faction != Enums.Faction.None &&
                (int)mission.faction != account.faction)
            {
                return false;
            }

            foreach (UnlockCondition condition in mission.conditions)
            {
                if (condition == null || !Satisfies(condition, account))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Satisfies(UnlockCondition condition, Account account)
        {
            switch (condition.type)
            {
                case ConditionType.Level:
                    return account.level >= condition.range.Min &&
                           account.level <= condition.range.Max;

                case ConditionType.Mission:

                    return condition.mission == null ||
                           account.achievements.Contains(
                               CompletionToken(condition.mission.index));

                case ConditionType.TotalFame:
                    return account.lifetimeFame >= condition.amount;

                case ConditionType.Prestige:
                    return account.prestige >= condition.amount;

                case ConditionType.TotalKills:
                    return account.lifetimeKills >= condition.amount;

                case ConditionType.LastArenaPosition:

                    return account.lastArenaPos > 0 &&
                           account.lastArenaPos <= condition.amount;

                case ConditionType.FounderTier:

                    return account.founderPack && condition.amount <= 1;

                case ConditionType.DayOfWeek:
                    return ServerDay() == condition.day;

                case ConditionType.MaxBoat:
                    return IsMaxBoat(account);

                case ConditionType.NotMaxBoat:
                    return !IsMaxBoat(account);

                case ConditionType.Achievement:
                    return condition.achievement == null ||
                           account.achievements.Contains(
                               condition.achievement.index + ":" + condition.achievementTier);

                case ConditionType.Admin:
                    return account.admin;

                case ConditionType.Moderator:
                    return account.admin;

                case ConditionType.Assigned:

                    return true;

                default:
                    return false;
            }
        }

        private static DaysOfWeek ServerDay()
        {
            DateTime now = DateTime.UtcNow;
            return now.Hour < 8
                ? (DaysOfWeek)(((int)now.DayOfWeek + 6) % 7)
                : (DaysOfWeek)(int)now.DayOfWeek;
        }

        private static bool IsMaxBoat(Account account)
        {
            return account.hullSlots == 7777777777L &&
                   account.shieldSlots == 5555555555L &&
                   account.turbineSlots == 5555555555L &&
                   account.weaponSlots == 5555555555L;
        }

        public static int TargetOfObjective(MissionObjective objective)
        {
            return objective == null ? 1 : Mathf.Max(1, objective.amount);
        }

        public static int[] TargetsOf(MissionData mission)
        {
            if (mission?.objectives == null || mission.objectives.Count == 0)
            {
                return new[] { 1 };
            }

            var targets = new int[mission.objectives.Count];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = TargetOfObjective(mission.objectives[i]);
            }

            return targets;
        }

        public static List<MissionReward> RewardsFor(MissionData mission, Account account)
        {
            if (mission == null)
            {
                return new List<MissionReward>();
            }

            if (!mission.isFactionReward)
            {
                return mission.rewards ?? new List<MissionReward>();
            }

            List<MissionReward> faction = (Enums.Faction)(account?.faction ?? 3) switch
            {
                Enums.Faction.Nautilus => mission.blueRewards,
                Enums.Faction.Serran => mission.redRewards,
                Enums.Faction.Azularis => mission.yellowRewards,
                _ => mission.blueRewards,
            };

            return faction ?? mission.rewards ?? new List<MissionReward>();
        }
    }
}
