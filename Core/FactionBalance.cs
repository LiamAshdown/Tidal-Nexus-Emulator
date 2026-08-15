using System;

namespace TidalNexus.StandaloneServer.Core
{

    public readonly struct FactionCensus
    {
        public static readonly FactionCensus Empty = default;

        public FactionCensus(int nautilus, int serran, int azularis)
        {
            Nautilus = nautilus > 0 ? nautilus : 0;
            Serran = serran > 0 ? serran : 0;
            Azularis = azularis > 0 ? azularis : 0;
        }

        public int Nautilus { get; }

        public int Serran { get; }

        public int Azularis { get; }

        public int Total => Nautilus + Serran + Azularis;

        public int Smallest => Math.Min(Nautilus, Math.Min(Serran, Azularis));

        public int CountOf(PlayerFaction faction)
        {
            switch (faction)
            {
                case PlayerFaction.Nautilus: return Nautilus;
                case PlayerFaction.Serran: return Serran;
                case PlayerFaction.Azularis: return Azularis;
                default: return 0;
            }
        }
    }

    public readonly struct FactionBalanceReport
    {
        public FactionBalanceReport(
            float nautilus, float serran, float azularis, int totalPlayers)
        {
            Nautilus = nautilus;
            Serran = serran;
            Azularis = azularis;
            TotalPlayers = totalPlayers;
        }

        public float Nautilus { get; }

        public float Serran { get; }

        public float Azularis { get; }

        public int TotalPlayers { get; }

        public bool Enforced => TotalPlayers > 0;
    }

    public static class FactionBalance
    {

        public const int Floor = 30;

        public const int Tolerance = 3;

        public static FactionBalanceReport Report(FactionCensus census)
        {
            int total = census.Total;

            if (total < Floor)
            {
                return new FactionBalanceReport(0f, 0f, 0f, 0);
            }

            float scale = 100f / total;

            return new FactionBalanceReport(
                census.Nautilus * scale,
                census.Serran * scale,
                census.Azularis * scale,
                total);
        }

        public static bool CanJoin(FactionCensus census, PlayerFaction faction)
        {
            if (faction != PlayerFaction.Nautilus
                && faction != PlayerFaction.Serran
                && faction != PlayerFaction.Azularis)
            {
                return false;
            }

            return census.CountOf(faction) - census.Smallest <= Tolerance;
        }
    }
}
