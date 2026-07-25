using System;

namespace CustomStrategies.Calculations
{
    public static class TzHelper
    {
        public static TimeZoneInfo GetEstTimeZone()
        {
            try { return CustomStrategies.Calculations.TzHelper.GetEstTimeZone(); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        }
    }
}
