using System;
using System.Collections.Generic;

namespace CustomStrategies.Calculations
{
    public class ShapeDetector
    {
        public void ClassifyShape(MarketData data, List<KeyValuePair<double, double>> sortedProfile, double tickSize, int doubleDistMinTicks, double maxVolume, double lvnThreshold = 0.12, double hvn2MinRatio = 0.30)
        {
            double range = data.IB_High - data.IB_Low;
            
            // Step 1: Initial classification based on POC position within the IB range
            if (data.IB_POC > data.IB_Low + (0.6 * range))
            {
                data.CurrentShape = VolumeProfileShape.PShape;
            }
            else if (data.IB_POC < data.IB_Low + (0.4 * range))
            {
                data.CurrentShape = VolumeProfileShape.bShape;
            }
            else
            {
                data.CurrentShape = VolumeProfileShape.DShape;
            }

            // Step 2: Search for a secondary High Volume Node (HVN2)
            // HVN2 must be at least doubleDistMinTicks away from the POC
            data.IB_HVN1 = data.IB_POC;
            double hvn2_vol = -1;
            double hvn2_price = -1;
            double minDistance = doubleDistMinTicks * tickSize;

            for (int i = 0; i < sortedProfile.Count; i++)
            {
                double price = sortedProfile[i].Key;
                double vol = sortedProfile[i].Value;

                if (Math.Abs(price - data.IB_HVN1) >= minDistance)
                {
                    double prevVol = i > 0 ? sortedProfile[i - 1].Value : 0;
                    double nextVol = i < sortedProfile.Count - 1 ? sortedProfile[i + 1].Value : 0;
                    
                    // Local peak: volume higher than both neighbors
                    if (vol > prevVol && vol > nextVol)
                    {
                        if (vol > hvn2_vol)
                        {
                            hvn2_vol = vol;
                            hvn2_price = price;
                        }
                    }
                }
            }

            // Step 3: Validate Double Distribution — requires BOTH conditions:
            //   A) HVN2 must have significant volume (at least hvn2MinRatio of POC volume)
            //   B) The valley (LVN) between HVN1 and HVN2 must be genuinely thin (below lvnThreshold of max)
            if (hvn2_price != -1 && hvn2_vol >= (hvn2MinRatio * maxVolume))
            {
                double lvn_vol = double.MaxValue;
                double lvn_price = -1;

                double startPrice = Math.Min(data.IB_HVN1, hvn2_price);
                double endPrice = Math.Max(data.IB_HVN1, hvn2_price);

                foreach (var node in sortedProfile)
                {
                    if (node.Key > startPrice && node.Key < endPrice)
                    {
                        if (node.Value < lvn_vol)
                        {
                            lvn_vol = node.Value;
                            lvn_price = node.Key;
                        }
                    }
                }

                if (lvn_price != -1 && lvn_vol < (lvnThreshold * maxVolume))
                {
                    data.CurrentShape = VolumeProfileShape.BShape;
                    data.IB_HVN2 = hvn2_price;
                    data.IB_LVN = lvn_price;
                }
            }
        }
    }
}
