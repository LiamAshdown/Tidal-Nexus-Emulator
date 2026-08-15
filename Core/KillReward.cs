using System;
using System.Collections.Generic;

namespace TidalNexus.StandaloneServer.Core
{

    public readonly struct RewardPurse
    {
        public static readonly RewardPurse Empty = default;

        public RewardPurse(int credits, int borax, int experience, int fame)
        {
            Credits = credits > 0 ? credits : 0;
            Borax = borax > 0 ? borax : 0;
            Experience = experience > 0 ? experience : 0;
            Fame = fame > 0 ? fame : 0;
        }

        public int Credits { get; }

        public int Borax { get; }

        public int Experience { get; }

        public int Fame { get; }

        public bool IsEmpty =>
            Credits == 0 && Borax == 0 && Experience == 0 && Fame == 0;
    }

    public readonly struct RewardShare
    {
        public RewardShare(
            int damage, double fraction, RewardPurse payout, bool roundedToNothing)
        {
            Damage = damage;
            Fraction = fraction;
            Payout = payout;
            RoundedToNothing = roundedToNothing;
        }

        public int Damage { get; }

        public double Fraction { get; }

        public RewardPurse Payout { get; }

        public bool RoundedToNothing { get; }
    }

    public static class KillReward
    {

        public static IReadOnlyList<RewardShare> Split(
            IReadOnlyList<int> damage, RewardPurse purse)
        {
            int count = damage?.Count ?? 0;
            if (count == 0)
            {
                return Array.Empty<RewardShare>();
            }

            long total = 0;
            for (int i = 0; i < count; i++)
            {
                if (damage[i] > 0)
                {
                    total += damage[i];
                }
            }

            var shares = new RewardShare[count];

            if (total <= 0)
            {
                for (int i = 0; i < count; i++)
                {
                    shares[i] = new RewardShare(0, 0d, RewardPurse.Empty, false);
                }

                return shares;
            }

            int[] credits = Apportion(damage, total, purse.Credits);
            int[] borax = Apportion(damage, total, purse.Borax);
            int[] experience = Apportion(damage, total, purse.Experience);
            int[] fame = Apportion(damage, total, purse.Fame);

            for (int i = 0; i < count; i++)
            {
                int dealt = damage[i] > 0 ? damage[i] : 0;
                var payout = new RewardPurse(
                    credits[i], borax[i], experience[i], fame[i]);

                shares[i] = new RewardShare(
                    dealt,
                    dealt / (double)total,
                    payout,
                    dealt > 0 && payout.IsEmpty && !purse.IsEmpty);
            }

            return shares;
        }

        private static int[] Apportion(IReadOnlyList<int> damage, long total, int pot)
        {
            int count = damage.Count;
            var payout = new int[count];

            if (pot <= 0)
            {
                return payout;
            }

            var remainder = new long[count];
            var contenders = new List<int>(count);
            long handed = 0;

            for (int i = 0; i < count; i++)
            {
                int dealt = damage[i];
                if (dealt <= 0)
                {
                    continue;
                }

                long exact = (long)pot * dealt;
                long whole = exact / total;

                payout[i] = (int)whole;
                remainder[i] = exact - (whole * total);
                handed += whole;
                contenders.Add(i);
            }

            long spare = pot - handed;
            if (spare <= 0)
            {
                return payout;
            }

            int[] order = contenders.ToArray();
            Array.Sort(order, (a, b) =>
            {
                int byRemainder = remainder[b].CompareTo(remainder[a]);

                return byRemainder != 0 ? byRemainder : a.CompareTo(b);
            });

            for (int i = 0; i < order.Length && spare > 0; i++, spare--)
            {
                payout[order[i]]++;
            }

            return payout;
        }
    }
}
