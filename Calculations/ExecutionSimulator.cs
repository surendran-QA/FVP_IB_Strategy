using System;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies.Calculations
{
    public class ExecutionSimulator
    {
        public void SimulateExecution(MarketData md, HistoricalData history, TimeSpan ibEndTime, TimeSpan sessionEndTime, TimeZoneInfo tz)
        {
            if (md.Signal == null || md.Signal.PreferredSide == "NONE" || double.IsNaN(md.Signal.EntryPrice))
                return;

            DateTime? targetDate = md.LastCalculatedDate;
            if (targetDate == null || targetDate == DateTime.MinValue)
                return;

            DateTime sessionEndIst = (sessionEndTime < ibEndTime) 
                ? targetDate.Value.Date.AddDays(1).Add(sessionEndTime)
                : targetDate.Value.Date.Add(sessionEndTime);

            DateTime ibEndIst = targetDate.Value.Date.Add(ibEndTime);

            // Loop through the history starting from ibEndIst up to sessionEndIst
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var bar = (HistoryItemBar)history[i];
                DateTime barIst = TimeZoneInfo.ConvertTimeFromUtc(bar.TimeLeft, tz);

                if (barIst < ibEndIst) continue;
                if (barIst > sessionEndIst) break;

                // 1. Check for Entry if Waiting
                if (md.Signal.Status == "Waiting")
                {
                    bool entryHit = false;
                    if (md.Signal.PreferredSide == "LONG" && bar.Low <= md.Signal.EntryPrice && bar.High >= md.Signal.EntryPrice)
                        entryHit = true;
                    else if (md.Signal.PreferredSide == "SHORT" && bar.High >= md.Signal.EntryPrice && bar.Low <= md.Signal.EntryPrice)
                        entryHit = true;

                    if (entryHit)
                    {
                        md.Signal.Status = "In Trade";
                        md.Signal.EntryTime = barIst;
                    }
                }
                
                // 2. Check for Exits if In Trade
                if (md.Signal.Status == "In Trade")
                {
                    // Check SL first for conservative simulation
                    bool slHit = false;
                    bool tpHit = false;

                    if (md.Signal.PreferredSide == "LONG")
                    {
                        if (bar.Low <= md.Signal.StopLoss) slHit = true;
                        if (bar.High >= md.Signal.TakeProfit) tpHit = true;
                    }
                    else if (md.Signal.PreferredSide == "SHORT")
                    {
                        if (bar.High >= md.Signal.StopLoss) slHit = true;
                        if (bar.Low <= md.Signal.TakeProfit) tpHit = true;
                    }

                    if (slHit)
                    {
                        md.Signal.Status = "Closed";
                        md.Signal.ExitReason = "SL Hit";
                        md.Signal.ExitTime = barIst;
                        break; // End simulation for this day
                    }
                    else if (tpHit)
                    {
                        md.Signal.Status = "Closed";
                        md.Signal.ExitReason = "TP Hit";
                        md.Signal.ExitTime = barIst;
                        break; // End simulation for this day
                    }
                }
            }

            // 3. EOD Check
            if (history.Count > 0)
            {
                DateTime lastAvailableBarIst = TimeZoneInfo.ConvertTimeFromUtc(((HistoryItemBar)history[0]).TimeLeft, tz);
                bool isDayEnded = lastAvailableBarIst >= sessionEndIst;

                if (isDayEnded)
                {
                    if (md.Signal.Status == "In Trade")
                    {
                        md.Signal.Status = "Closed";
                        md.Signal.ExitReason = "EOD";
                        md.Signal.ExitTime = sessionEndIst;
                    }
                    else if (md.Signal.Status == "Waiting")
                    {
                        md.Signal.Status = "Cancelled";
                        md.Signal.ExitReason = "Not Triggered";
                    }
                }
            }
        }
    }
}
