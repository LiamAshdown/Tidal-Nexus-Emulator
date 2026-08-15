namespace TidalNexus.StandaloneServer.Core
{

    public static class EquipmentSlots
    {

        public const int Count = 10;

        public const long Default = 1000000000L;

        public static int DigitCount(long slots)
        {
            if (slots < 0)
            {
                return 0;
            }

            int digits = 1;
            while (slots >= 10)
            {
                slots /= 10;
                digits++;
            }

            return digits;
        }

        public static int Fitted(long slots, int position)
        {
            if (slots < 0 || position < 0)
            {
                return 0;
            }

            int digits = DigitCount(slots);
            if (position >= digits)
            {
                return 0;
            }

            long divisor = 1;
            for (int step = digits - 1 - position; step > 0; step--)
            {
                divisor *= 10;
            }

            return (int)(slots / divisor % 10);
        }

        public static int FittedInSlotZero(long slots)
        {
            return Fitted(slots, 0);
        }

        public static bool TryWrite(long slots, int position, int tier, out long updated)
        {
            updated = slots;

            if (position < 0 || position >= Count || tier < 0 || tier > 9)
            {
                return false;
            }

            if (slots < 0 || DigitCount(slots) > Count)
            {
                return false;
            }

            var digits = new int[Count];
            int stored = DigitCount(slots);
            for (int i = 0; i < stored; i++)
            {
                digits[i] = Fitted(slots, i);
            }

            digits[position] = tier;

            if (digits[0] == 0)
            {
                return false;
            }

            long packed = 0;
            for (int i = 0; i < Count; i++)
            {
                packed = packed * 10 + digits[i];
            }

            updated = packed;
            return true;
        }

        public static bool CanClientRead(long slots, int slotCount)
        {
            return slots >= 0 && DigitCount(slots) >= slotCount;
        }

        public static bool IsWellFormed(long slots)
        {
            return slots > 0 && DigitCount(slots) == Count;
        }
    }
}
