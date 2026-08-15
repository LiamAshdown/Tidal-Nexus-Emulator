using System;

namespace TidalNexus.StandaloneServer.Core
{

    public static class EventSchedule
    {
        public const int Days = 7;
        public const int HoursPerDay = 24;

        public const int Slots = Days * HoursPerDay;

        public static int DayIndex(DayOfWeek day)
        {
            return ((int)day + 6) % Days;
        }

        public static void CellAt(DateTime utc, int offsetHours, out int day, out int hour)
        {
            DateTime local = utc.AddHours(offsetHours);

            day = DayIndex(local.DayOfWeek);
            hour = local.Hour;
        }

        public static int SlotIndex(int day, int hour)
        {
            return day * HoursPerDay + hour;
        }

        public static int SlotAt(DateTime utc, int offsetHours)
        {
            CellAt(utc, offsetHours, out int day, out int hour);
            return SlotIndex(day, hour);
        }
    }
}
