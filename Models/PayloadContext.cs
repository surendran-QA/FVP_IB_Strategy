using System;

namespace CustomStrategies.Models
{
    public class PayloadContext
    {
        public string StrategyName { get; set; }
        public string Symbol { get; set; }
        public DateTime EstTime { get; set; }
        public string Shape { get; set; }
        public string Bias { get; set; }
        public string Status { get; set; }
        public string Result { get; set; }
        public string IbHvn1 { get; set; }
        public string IbHvn2 { get; set; }
        public string IbLvn { get; set; }
        public double IbHigh { get; set; }
        public double IbLow { get; set; }
        public double IbPoc { get; set; }
        public double IbVah { get; set; }
        public double IbVal { get; set; }
        public double TotalVolume { get; set; }
        public double EntryPrice { get; set; }
        public double StopLoss { get; set; }
        public double TakeProfit { get; set; }
        public double SessionHigh { get; set; }
        public double SessionLow { get; set; }
        public double NyOpen { get; set; }
        public bool EnableWebhook { get; set; }
        public bool AutoCognify { get; set; }
    }
}
