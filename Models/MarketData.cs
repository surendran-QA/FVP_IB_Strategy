using System;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies
{
    public class MarketData
    {
        public double IB_High { get; set; } = double.MinValue;
        public double IB_Low { get; set; } = double.MaxValue;
        public double IB_TotalVolume { get; set; } = 0;
        public double IB_POC { get; set; } = 0;
        public double IB_VAH { get; set; } = 0;
        public double IB_VAL { get; set; } = 0;
        
        public VolumeProfileShape CurrentShape { get; set; } = VolumeProfileShape.Unknown;
        public double IB_HVN1 { get; set; } = double.NaN;
        public double IB_HVN2 { get; set; } = double.NaN;
        public double IB_LVN { get; set; } = double.NaN;
        
        public bool IsIBCalculated { get; set; } = false;
        public DateTime LastCalculatedDate { get; set; } = DateTime.MinValue;
        
        public Calculations.TradeSignal Signal { get; set; } = new Calculations.TradeSignal();
    }
}
