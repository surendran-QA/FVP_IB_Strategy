using System;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies.Calculations
{
    public class DataIngestionService
    {
        public List<HistoryItemBar> GetProfileBars(HistoricalData hdm, DateTime targetDate, out double ibHigh, out double ibLow)
        {
            var profileBars = new List<HistoryItemBar>();
            ibHigh = double.MinValue;
            ibLow = double.MaxValue;

            var currentSimDate = targetDate.Date;
            
            // DST Fix: Natively request Exchange Time (Eastern Standard Time)
            // EST automatically handles the shift to EDT in the summer.
            TimeZoneInfo estTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            
            // NOTE: hdm[0] is the NEWEST bar, hdm[Count-1] is OLDEST.
            // We iterate newest→oldest. The early break fires when we pass the target date.
            for (int i = 0; i < hdm.Count; i++)
            {
                var bar = (HistoryItemBar)hdm[i];
                DateTime barEst = TimeZoneInfo.ConvertTimeFromUtc(bar.TimeLeft, estTz);

                if (barEst.Date == currentSimDate)
                {
                    TimeSpan t = barEst.TimeOfDay;
                    // US Equity Market Open: 9:30 AM to 10:00 AM Eastern Time
                    if (t >= new TimeSpan(9, 30, 0) && t < new TimeSpan(10, 0, 0))
                    {
                        profileBars.Add(bar);
                        if (bar.High > ibHigh) ibHigh = bar.High;
                        if (bar.Low < ibLow) ibLow = bar.Low;
                    }
                }

                if (barEst.Date < currentSimDate) break;
            }

            return profileBars;
        }
    }
}
