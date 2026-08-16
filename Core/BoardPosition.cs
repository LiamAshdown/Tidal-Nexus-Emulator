using System.Collections.Generic;

namespace TidalNexus.StandaloneServer.Core
{

    public static class BoardPosition
    {

        public static int Of(IReadOnlyList<long> descending, long score)
        {
            if (descending == null || score <= 0)
            {
                return PrestigeRanks.Unranked;
            }

            int low = 0;
            int high = descending.Count;

            while (low < high)
            {
                int mid = low + ((high - low) >> 1);

                if (descending[mid] > score)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low + 1;
        }

        public static float AsPercentage(int position, int population)
        {
            if (position <= PrestigeRanks.Unranked || population <= 0)
            {
                return 0f;
            }

            if (position > population)
            {
                population = position;
            }

            return (float)(100.0 * position / population);
        }
    }
}
