using System.Collections.Generic;

namespace TidalNexus.StandaloneServer.Core
{

    public enum CargoRefusal
    {
        None = 0,

        BadAmount,

        NoRoom,
    }

    public readonly struct CargoOffer
    {
        private CargoOffer(long units, long free, CargoRefusal refusal)
        {
            Units = units;
            Free = free;
            Refusal = refusal;
        }

        internal static CargoOffer Refused(long free, CargoRefusal reason) =>
            new CargoOffer(0, free, reason == CargoRefusal.None ? CargoRefusal.NoRoom : reason);

        internal static CargoOffer Accepted(long units, long free) =>
            new CargoOffer(units, free, CargoRefusal.None);

        public long Units { get; }

        public long Free { get; }

        public CargoRefusal Refusal { get; }

        public bool Fits => Refusal == CargoRefusal.None;
    }

    public enum CollectOutcome
    {

        TakeAll,

        Empty,

        HoldFull,
    }

    public readonly struct CollectDecision
    {
        private CollectDecision(CollectOutcome outcome, long units, long free)
        {
            Outcome = outcome;
            Units = units;
            Free = free;
        }

        internal static CollectDecision Of(CollectOutcome outcome, long units, long free) =>
            new CollectDecision(outcome, units, free);

        public CollectOutcome Outcome { get; }

        public long Units { get; }

        public long Free { get; }

        public bool TakesEverything => Outcome == CollectOutcome.TakeAll;

        public bool HoldFull => Outcome == CollectOutcome.HoldFull;

        public bool ClearsTheWreck => Outcome != CollectOutcome.HoldFull;
    }

    public static class CargoHold
    {

        public static long FreeSpace(int used, int capacity)
        {
            long room = (long)capacity - (used > 0 ? used : 0);
            return room > 0 ? room : 0;
        }

        public static bool Takeable(int amount) => amount > 0;

        public static long Contents(IReadOnlyList<int> amounts)
        {
            if (amounts == null)
            {
                return 0;
            }

            long total = 0;
            for (int i = 0; i < amounts.Count; i++)
            {
                int amount = amounts[i];
                if (!Takeable(amount))
                {
                    continue;
                }

                if (total > long.MaxValue - amount)
                {
                    return long.MaxValue;
                }

                total += amount;
            }

            return total;
        }

        public static CargoOffer Offer(int used, int capacity, int amount)
        {
            long free = FreeSpace(used, capacity);

            if (!Takeable(amount))
            {
                return CargoOffer.Refused(free, CargoRefusal.BadAmount);
            }

            return amount > free
                ? CargoOffer.Refused(free, CargoRefusal.NoRoom)
                : CargoOffer.Accepted(amount, free);
        }

        public static CollectDecision Collect(
            int used, int capacity, IReadOnlyList<int> amounts)
        {
            long free = FreeSpace(used, capacity);
            long total = Contents(amounts);

            if (total <= 0)
            {
                return CollectDecision.Of(CollectOutcome.Empty, 0, free);
            }

            return total > free
                ? CollectDecision.Of(CollectOutcome.HoldFull, total, free)
                : CollectDecision.Of(CollectOutcome.TakeAll, total, free);
        }
    }
}
