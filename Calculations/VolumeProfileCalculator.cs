using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies.Calculations
{
    public class VolumeProfileCalculator
    {
        public bool CalculateProfile(List<HistoryItemBar> profileBars, Symbol currentSymbol, int profileStepTicks, double ibLow, out MarketData data, out bool isPrecise, out List<KeyValuePair<double, double>> sortedProfile, out double maxVolume)
        {
            isPrecise = true;
            sortedProfile = null;
            maxVolume = -1;
            data = new MarketData();
            data.IB_High = profileBars.Count > 0 ? profileBars.Max(b => b.High) : 0;
            data.IB_Low = ibLow;

            Dictionary<double, double> volumeProfile = new Dictionary<double, double>();
            if (profileBars.Count == 0) return false;

            double binSize = currentSymbol.TickSize * profileStepTicks;

            foreach (var bar in profileBars)
            {
                if (bar.VolumeAnalysisData != null && bar.VolumeAnalysisData.PriceLevels != null && bar.VolumeAnalysisData.PriceLevels.Count > 0)
                {
                    foreach (var level in bar.VolumeAnalysisData.PriceLevels)
                    {
                        double price = level.Key;
                        double vol = level.Value.GetValue(VolumeAnalysisField.Volume);

                        double binnedPrice = Math.Floor((price - data.IB_Low) / binSize) * binSize + data.IB_Low;

                        if (!volumeProfile.ContainsKey(binnedPrice))
                            volumeProfile[binnedPrice] = 0;

                        volumeProfile[binnedPrice] += vol;
                    }
                }
                else
                {
                    isPrecise = false;
                    int ticksInBar = (int)Math.Round((bar.High - bar.Low) / currentSymbol.TickSize) + 1;
                    double volPerTick = bar.Volume / (double)ticksInBar;

                    for (int step = 0; step < ticksInBar; step++)
                    {
                        double priceLevel = bar.Low + (step * currentSymbol.TickSize);
                        double binnedPrice = Math.Floor((priceLevel - data.IB_Low) / binSize) * binSize + data.IB_Low;

                        if (!volumeProfile.ContainsKey(binnedPrice))
                            volumeProfile[binnedPrice] = 0;

                        volumeProfile[binnedPrice] += volPerTick;
                    }
                }
            }

            if (volumeProfile.Count == 0) return false;

            sortedProfile = volumeProfile.OrderBy(x => x.Key).ToList();
            maxVolume = -1;
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
            data.IB_TotalVolume = totalVol;
            
            // Note: Caller is responsible for Shape Detection and Value Area Calculation.
            // Or we can calculate Value Area here, since this is the Profile Calculator.
            
            // Value Area Calculation (70%) with Tie-Breaker Crash Fix
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
                else if (downVol > upVol)
                {
                    downIdx--;
                    currentAreaVol += downVol;
                }
                else
                {
                    // TIE-BREAKER CRASH FIX:
                    // If exactly equal, expand symmetrically
                    upIdx++;
                    downIdx--;
                    currentAreaVol += (upVol + downVol);
                }
            }

            data.IB_VAH = sortedProfile[upIdx].Key;
            data.IB_VAL = sortedProfile[downIdx].Key;
            
            return true;
        }
    }
}
