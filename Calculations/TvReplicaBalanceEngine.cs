using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies.Calculations
{
    public class TvReplicaBalanceEngine
    {
        public bool CalculateIB(Symbol symbol, DateTime targetDate, TimeSpan profileStart, TimeSpan profileEnd, int profileStepTicks, out MarketData data)
        {
            data = new MarketData();

            Dictionary<double, double> volumeProfile = new Dictionary<double, double>();
            TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            
            DateTime profileStartIst = targetDate.Add(profileStart);
            DateTime execStartIst = targetDate.Add(profileEnd);

            var min1Hist = symbol.GetHistory(
                Period.MIN1, 
                symbol.HistoryType,
                TimeZoneInfo.ConvertTimeToUtc(profileStartIst, istTz),
                TimeZoneInfo.ConvertTimeToUtc(execStartIst, istTz));

            if (min1Hist.Count == 0)
            {
                min1Hist.Dispose();
                return false;
            }

            for (int i = 0; i < min1Hist.Count; i++)
            {
                var bar = (HistoryItemBar)min1Hist[i];
                if (bar.High > data.IB_High) data.IB_High = bar.High;
                if (bar.Low < data.IB_Low) data.IB_Low = bar.Low;
            }

            double tickSize = symbol != null ? symbol.TickSize : 0.25;
            double binSize = tickSize * profileStepTicks;

            for (int i = 0; i < min1Hist.Count; i++)
            {
                var bar = (HistoryItemBar)min1Hist[i];
                
                int ticksInBar = (int)Math.Round((bar.High - bar.Low) / tickSize) + 1;
                double volPerTick = bar.Volume / (double)ticksInBar;

                for (int step = 0; step < ticksInBar; step++)
                {
                    double priceLevel = bar.Low + (step * tickSize);
                    
                    // TV REPLICA LOGIC: Absolute Zero Alignment
                    double binnedPrice = Math.Floor(priceLevel / binSize) * binSize;

                    if (!volumeProfile.ContainsKey(binnedPrice))
                        volumeProfile[binnedPrice] = 0;

                    volumeProfile[binnedPrice] += volPerTick;
                }
            }
            
            min1Hist.Dispose();

            if (volumeProfile.Count == 0) return false;

            var sortedProfile = volumeProfile.OrderBy(x => x.Key).ToList();
            double maxVolume = -1;
            int pocIndex = -1;
            double totalVol = 0;

            for (int i = 0; i < sortedProfile.Count; i++)
            {
                totalVol += sortedProfile[i].Value;
                if (sortedProfile[i].Value > maxVolume)
                {
                    maxVolume = sortedProfile[i].Value;
                    pocIndex = i;
                }
            }

            data.IB_POC = sortedProfile[pocIndex].Key;
            double valueAreaVol = totalVol * 0.70;
            double currentAreaVol = maxVolume;
            int upIdx = pocIndex;
            int downIdx = pocIndex;

            while (currentAreaVol < valueAreaVol)
            {
                double upVol = upIdx < sortedProfile.Count - 1 ? sortedProfile[upIdx + 1].Value : 0;
                double downVol = downIdx > 0 ? sortedProfile[downIdx - 1].Value : 0;

                if (upVol == 0 && downVol == 0) break;

                if (upVol > downVol)
                {
                    upIdx++;
                    currentAreaVol += upVol;
                }
                else
                {
                    downIdx--;
                    currentAreaVol += downVol;
                }
            }

            data.IB_VAH = sortedProfile[upIdx].Key;
            data.IB_VAL = sortedProfile[downIdx].Key;
            
            ShapeDetector _shapeDetector = new ShapeDetector();
            _shapeDetector.ClassifyShape(data, sortedProfile, symbol.TickSize, 40, maxVolume);

            SignalGenerator _signalGen = new SignalGenerator();
            data.Signal = _signalGen.GenerateSignal(data);

            data.IsIBCalculated = true;
            data.LastCalculatedDate = targetDate.Date;

            return true;
        }
    }
}
