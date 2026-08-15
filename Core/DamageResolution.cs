using System;

namespace TidalNexus.StandaloneServer.Core
{

    public readonly struct HitPoints
    {
        public HitPoints(int shield, int hull)
        {
            Shield = shield > 0 ? shield : 0;
            Hull = hull > 0 ? hull : 0;
        }

        public int Shield { get; }

        public int Hull { get; }

        public bool IsDestroyed => Hull == 0;
    }

    public readonly struct DamageOutcome
    {
        internal DamageOutcome(
            HitPoints before, HitPoints after, int absorbedByShield, int dealtToHull)
        {
            Before = before;
            After = after;
            AbsorbedByShield = absorbedByShield;
            DealtToHull = dealtToHull;
        }

        public HitPoints Before { get; }

        public HitPoints After { get; }

        public int AbsorbedByShield { get; }

        public int DealtToHull { get; }

        public int TotalDealt => AbsorbedByShield + DealtToHull;

        public bool Landed => TotalDealt > 0;

        public bool Fatal => !Before.IsDestroyed && After.IsDestroyed;
    }

    public static class DamageResolution
    {

        public static DamageOutcome Resolve(HitPoints target, int amount)
        {
            if (amount <= 0)
            {
                return new DamageOutcome(target, target, 0, 0);
            }

            int absorbed = Math.Min(target.Shield, amount);
            int remaining = amount - absorbed;
            int dealt = Math.Min(target.Hull, remaining);

            var after = new HitPoints(target.Shield - absorbed, target.Hull - dealt);

            return new DamageOutcome(target, after, absorbed, dealt);
        }
    }
}
