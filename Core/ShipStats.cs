using System;
using System.Collections.Generic;

namespace TidalNexus.StandaloneServer.Core
{

    public static class ShipStats
    {

        public const int MaxLevel = 100;

        public const float BaseSpeed = 2f;

        public const float TurbineSpeedScale = 0.002f;

        public static int SlotCount(int level)
        {
            if (level >= 60) { return 10; }
            if (level >= 52) { return 9; }
            if (level >= 45) { return 8; }
            if (level >= 38) { return 7; }
            if (level >= 30) { return 6; }
            if (level >= 22) { return 5; }
            if (level >= 15) { return 4; }
            if (level >= 10) { return 3; }
            if (level >= 5) { return 2; }
            return 1;
        }

        public static int LevelForExperience(long experience, IReadOnlyList<long> thresholds)
        {
            if (thresholds == null || thresholds.Count == 0 || experience < 0)
            {
                return 1;
            }

            int cap = thresholds.Count < MaxLevel ? thresholds.Count : MaxLevel;
            int level = 1;
            while (level < cap && thresholds[level - 1] <= experience)
            {
                level++;
            }

            return level;
        }

        public static long ExperienceForLevel(int level, IReadOnlyList<long> thresholds)
        {
            if (thresholds == null || thresholds.Count == 0)
            {
                return 0;
            }

            int clamped = level < 1 ? 1 : level;
            if (clamped > thresholds.Count)
            {
                clamped = thresholds.Count;
            }

            return thresholds[clamped - 1];
        }

        public static void LevelProgress(long experience, int level,
            IReadOnlyList<long> thresholds, out long into, out long span)
        {
            long floor = level <= 1 ? 0 : ExperienceForLevel(level - 1, thresholds);
            long ceiling = ExperienceForLevel(level, thresholds);

            into = experience > floor ? experience - floor : 0;
            span = ceiling > floor ? ceiling - floor : 1;

            if (into > span)
            {
                into = span;
            }
        }

        public static float Speed(long turbineSlots, int level, IReadOnlyList<int> turbineSpeeds)
        {
            float speed = BaseSpeed;
            int slots = SlotCount(level);

            for (int slot = 0; slot < slots; slot++)
            {
                speed += (float)Authored(turbineSlots, slot, turbineSpeeds) * TurbineSpeedScale;
            }

            return speed;
        }

        public static int MaxHull(long hullSlots, int level, IReadOnlyList<int> hullPoints)
        {
            return SumFitted(hullSlots, level, hullPoints);
        }

        public static int MaxShield(long shieldSlots, int level, IReadOnlyList<int> hullPoints)
        {
            return SumFitted(shieldSlots, level, hullPoints);
        }

        public static int PveDamage(long weaponSlots, int level, IReadOnlyList<int> weaponDamage)
        {
            return SumFitted(weaponSlots, level, weaponDamage);
        }

        public static int PvpDamage(int pveDamage, int level)
        {
            if (pveDamage <= 0)
            {
                return 0;
            }

            long scaled = (long)pveDamage * (level < 1 ? 1 : level);
            long total = (long)pveDamage + RoundToInt((float)scaled * TenthOfAPercent);

            return Saturate(total);
        }

        private const float TenthOfAPercent = 0.001f;

        public static int AccountHullMax(int level, int hpx)
        {
            return LevelCurve(level, hpx);
        }

        public static int AccountShieldMax(int level, int spx)
        {
            return LevelCurve(level, spx);
        }

        public static int AccountCargoMax(int level)
        {
            long capacity = 500L + (long)(level < 1 ? 1 : level) * 15L;
            return Saturate(capacity);
        }

        private static int LevelCurve(int level, int points)
        {
            long safeLevel = level < 1 ? 1 : level;
            long safePoints = points < 0 ? 0 : points;

            return Saturate(3000L + (safeLevel - 1) * 130L + safePoints * 250L);
        }

        public static int RoundToInt(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            double rounded = Math.Round((double)value, MidpointRounding.ToEven);

            if (rounded >= int.MaxValue) { return int.MaxValue; }
            if (rounded <= int.MinValue) { return int.MinValue; }

            return (int)rounded;
        }

        private static int Authored(long slots, int slot, IReadOnlyList<int> table)
        {
            if (table == null)
            {
                return 0;
            }

            int index = EquipmentSlots.Fitted(slots, slot);
            return index >= 0 && index < table.Count ? table[index] : 0;
        }

        private static int SumFitted(long slots, int level, IReadOnlyList<int> table)
        {
            long total = 0;
            int count = SlotCount(level);

            for (int slot = 0; slot < count; slot++)
            {
                total += Authored(slots, slot, table);
            }

            return Saturate(total);
        }

        private static int Saturate(long value)
        {
            if (value > int.MaxValue) { return int.MaxValue; }
            if (value < int.MinValue) { return int.MinValue; }
            return (int)value;
        }
    }
}
