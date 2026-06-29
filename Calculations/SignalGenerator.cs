using System;

namespace CustomStrategies.Calculations
{
    public class TradeSignal
    {
        public string PreferredSide { get; set; } = "NONE";
        public double EntryPrice { get; set; } = double.NaN;
        public double TakeProfit { get; set; } = double.NaN;
        public double StopLoss { get; set; } = double.NaN;
        
        // Execution Tracking
        public string Status { get; set; } = "Waiting";
        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public string ExitReason { get; set; }
    }

    public class SignalGenerator
    {
        public TradeSignal GenerateSignal(MarketData data, double tickSize = 0.25, int profileStepTicks = 4)
        {
            TradeSignal signal = new TradeSignal();

            if (data.CurrentShape == VolumeProfileShape.Unknown)
                return signal;

            double range = data.IB_High - data.IB_Low;
            double binOffset = tickSize * profileStepTicks;

            switch (data.CurrentShape)
            {
                case VolumeProfileShape.PShape:
                    signal.PreferredSide = "LONG";
                    signal.EntryPrice = data.IB_POC;
                    signal.StopLoss = data.IB_VAL;
                    signal.TakeProfit = data.IB_High;
                    break;

                case VolumeProfileShape.bShape:
                    signal.PreferredSide = "SHORT";
                    signal.EntryPrice = data.IB_POC;
                    signal.StopLoss = data.IB_VAH;
                    signal.TakeProfit = data.IB_Low;
                    break;

                case VolumeProfileShape.DShape:
                    signal.PreferredSide = "FADE";
                    signal.EntryPrice = data.IB_VAL; // Simplifying to long entry for fade
                    signal.StopLoss = data.IB_Low;
                    signal.TakeProfit = data.IB_POC;
                    break;

                case VolumeProfileShape.BShape:
                    if (!double.IsNaN(data.IB_LVN))
                    {
                        // If HVN1 (POC) is above LVN, we are breaking down into the lower distribution (HVN2)
                        if (data.IB_HVN1 > data.IB_LVN)
                        {
                            signal.PreferredSide = "SHORT";
                            // Offset entry upwards toward the POC by 1 bin to ensure fill before the vacuum
                            signal.EntryPrice = data.IB_LVN + binOffset;
                            signal.StopLoss = data.IB_HVN1; 
                            signal.TakeProfit = data.IB_HVN2 != -1 ? data.IB_HVN2 : data.IB_LVN - (range * 0.5);
                        }
                        else
                        {
                            // If HVN1 is below LVN, we are breaking up into the upper distribution (HVN2)
                            signal.PreferredSide = "LONG";
                            // Offset entry downwards toward the POC by 1 bin
                            signal.EntryPrice = data.IB_LVN - binOffset;
                            signal.StopLoss = data.IB_HVN1;
                            signal.TakeProfit = data.IB_HVN2 != -1 ? data.IB_HVN2 : data.IB_LVN + (range * 0.5);
                        }
                    }
                    else
                    {
                        signal.EntryPrice = data.IB_POC;
                        signal.StopLoss = data.IB_POC - range * 0.2;
                        signal.TakeProfit = data.IB_POC + range * 0.5;
                    }
                    break;
            }

            return signal;
        }

        public void UpdateSignalStatus(TradeSignal signal, double currentHigh, double currentLow)
        {
            if (signal.Status == "Waiting")
            {
                if (currentLow <= signal.EntryPrice && currentHigh >= signal.EntryPrice)
                {
                    signal.Status = "In Trade";
                }
            }

            if (signal.Status == "In Trade")
            {
                if (signal.PreferredSide == "LONG" || signal.PreferredSide == "FADE" || signal.PreferredSide == "BREAKOUT")
                {
                    if (currentLow <= signal.StopLoss)
                        signal.Status = "SL Hit";
                    else if (currentHigh >= signal.TakeProfit)
                        signal.Status = "TP Hit";
                }
                else if (signal.PreferredSide == "SHORT")
                {
                    if (currentHigh >= signal.StopLoss)
                        signal.Status = "SL Hit";
                    else if (currentLow <= signal.TakeProfit)
                        signal.Status = "TP Hit";
                }
            }
        }
    }
}
