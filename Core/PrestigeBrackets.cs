using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TidalNexus.StandaloneServer.Core
{

    public readonly struct FameStanding
    {
        public FameStanding(PlayerFaction faction, long weeklyFame)
        {
            Faction = faction;
            WeeklyFame = weeklyFame;
        }

        public PlayerFaction Faction { get; }

        public long WeeklyFame { get; }
    }

    public readonly struct BracketCutoff
    {
        public BracketCutoff(int bracket, int nautilus, int serran, int azularis)
        {
            Bracket = bracket;
            Nautilus = nautilus;
            Serran = serran;
            Azularis = azularis;
        }

        public int Bracket { get; }

        public int Nautilus { get; }

        public int Serran { get; }

        public int Azularis { get; }
    }

    public static class PrestigeBrackets
    {

        public const int Rows = 12;

        public const int FirstBracket = 2;

        private static readonly int[] Boundaries =
        {
            3, 10, 24, 45, 85, 146, 230, 342, 479, 630, 800, 1000,
        };

        private static readonly ReadOnlyCollection<int> Published =
            Array.AsReadOnly(Boundaries);

        public static IReadOnlyList<int> PercentilesPerMille => Published;

        public static IReadOnlyList<BracketCutoff> Table(
            IEnumerable<FameStanding> population)
        {
            var nautilus = new List<long>();
            var serran = new List<long>();
            var azularis = new List<long>();

            if (population != null)
            {
                foreach (FameStanding standing in population)
                {

                    if (standing.WeeklyFame <= 0)
                    {
                        continue;
                    }

                    switch (standing.Faction)
                    {
                        case PlayerFaction.Nautilus:
                            nautilus.Add(standing.WeeklyFame);
                            break;
                        case PlayerFaction.Serran:
                            serran.Add(standing.WeeklyFame);
                            break;
                        case PlayerFaction.Azularis:
                            azularis.Add(standing.WeeklyFame);
                            break;
                    }
                }
            }

            SortDescending(nautilus);
            SortDescending(serran);
            SortDescending(azularis);

            var rows = new BracketCutoff[Rows];
            for (int i = 0; i < Rows; i++)
            {
                int perMille = Boundaries[i];
                rows[i] = new BracketCutoff(
                    FirstBracket + i,
                    CutoffAt(nautilus, perMille),
                    CutoffAt(serran, perMille),
                    CutoffAt(azularis, perMille));
            }

            return rows;
        }

        public static int CutoffAt(IReadOnlyList<long> descending, int perMille)
        {
            int count = descending == null ? 0 : descending.Count;
            if (count == 0)
            {
                return 0;
            }

            long rank = (((long)count * perMille + 999) / 1000) - 1;

            if (rank < 0)
            {
                rank = 0;
            }
            else if (rank > count - 1)
            {
                rank = count - 1;
            }

            return (int)Math.Min(descending[(int)rank], int.MaxValue);
        }

        private static void SortDescending(List<long> fame)
        {
            fame.Sort((x, y) => y.CompareTo(x));
        }
    }
}
