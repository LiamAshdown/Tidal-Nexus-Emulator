using System;
using TidalNexus.StandaloneServer.Data;

namespace TidalNexus.StandaloneServer.Services
{

    public static class Unlocks
    {
        public static bool Meets(Unlockable unlockable, Account account)
        {
            if (unlockable == null)
            {
                return false;
            }

            if (account == null)
            {
                return true;
            }

            if (unlockable.faction != Enums.Faction.None &&
                (int)unlockable.faction != account.faction)
            {
                return false;
            }

            if (unlockable.conditions == null || unlockable.conditions.Count == 0)
            {
                return true;
            }

            foreach (UnlockCondition condition in unlockable.conditions)
            {
                if (condition == null || !Satisfies(condition, unlockable, account))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Satisfies(UnlockCondition condition, Unlockable unlockable,
            Account account)
        {
            switch (condition.type)
            {
                case ConditionType.Level:
                    return account.level >= condition.range.Min &&
                           account.level <= condition.range.Max;

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

                case ConditionType.Mission:
                    return condition.mission == null ||
                           account.achievements.Contains("mission_" + condition.mission.index);

                case ConditionType.Admin:
                case ConditionType.Moderator:
                    return account.admin;

                case ConditionType.Assigned:
                case ConditionType.Achievement:
                    return Owns(unlockable, account);

                default:
                    return false;
            }
        }

        private static bool Owns(Unlockable unlockable, Account account)
        {
            switch (unlockable)
            {
                case TitleData title: return account.titles.Contains(title.index);
                case SkinData skin: return account.skins.Contains(skin.index);
                case ShipData ship: return account.designs.Contains(ship.index);
                case SentryDesignData sentry: return account.sentryDesigns.Contains(sentry.index);
                case HpxDesignData hpx: return account.hpxDesigns.Contains(hpx.index);
                case SpxDesignData spx: return account.spxDesigns.Contains(spx.index);
                default: return false;
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
    }
}
