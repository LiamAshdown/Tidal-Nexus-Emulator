using System.Collections.Generic;

namespace TidalNexus.StandaloneServer.Core
{

    public static class PrestigeRanks
    {

        public const int NoRank = -1;

        public const int WornAsTitleIndex = 0;

        public const int Unreachable = int.MaxValue;

        public static int HighestRankMet(int prestige, IReadOnlyList<int> requiredPrestige)
        {
            if (requiredPrestige == null)
            {
                return NoRank;
            }

            int held = NoRank;
            int highestMet = 0;

            for (int rank = 0; rank < requiredPrestige.Count; rank++)
            {
                int required = requiredPrestige[rank];

                if (prestige < required)
                {
                    continue;
                }

                if (held == NoRank || required > highestMet)
                {
                    held = rank;
                    highestMet = required;
                }
            }

            return held;
        }

        public static bool HoldsAnyRank(int prestige, IReadOnlyList<int> requiredPrestige)
        {
            return HighestRankMet(prestige, requiredPrestige) != NoRank;
        }

        public const int Unranked = 0;

        public const int TopOfFaction = 1;

        public const int LowestBracket = 13;

        public const int FirstRankedBracket = 2;

        public const int BeyondTheTable = 14;

        public static int BracketFor(
            int position, int population, IReadOnlyList<int> percentilesPerMille)
        {
            if (position <= Unranked || population <= 0)
            {
                return Unranked;
            }

            if (position == TopOfFaction)
            {
                return TopOfFaction;
            }

            if (percentilesPerMille == null)
            {
                return BeyondTheTable;
            }

            for (int i = 0; i < percentilesPerMille.Count; i++)
            {
                if ((long)position * 1000L <= (long)percentilesPerMille[i] * population)
                {
                    return FirstRankedBracket + i;
                }
            }

            return BeyondTheTable;
        }

        private static readonly int[] AwardByBracket =
        {
            14000, 13000, 12000, 11000, 10000, 9000, 8000,
            7000, 6000, 5000, 4000, 3000, 2000,
        };

        public static int WeeklyAward(int bracket)
        {
            if (bracket < TopOfFaction || bracket > LowestBracket)
            {
                return 0;
            }

            return AwardByBracket[bracket - TopOfFaction];
        }

        public const int RetainedPerCent = 95;

        public static int AfterWeeklyReset(int prestige, int bracket)
        {
            long retained = (long)prestige * RetainedPerCent / 100L;
            long total = retained + WeeklyAward(bracket);

            if (total > int.MaxValue) { return int.MaxValue; }
            if (total < int.MinValue) { return int.MinValue; }

            return (int)total;
        }
    }
}
