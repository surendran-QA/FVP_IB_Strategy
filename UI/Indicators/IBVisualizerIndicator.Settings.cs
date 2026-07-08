using System;
using System.Collections.Generic;
using System.Drawing;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;
using FVP_IB_Strategy.Calculations;

namespace CustomStrategies
{
    public partial class IBVisualizerIndicator
    {
        [InputParameter("Start Trading Time (EST)", 0)]
        public TimeSpan StartTradingTime { get; set; } = new TimeSpan(10, 0, 0);

        [InputParameter("End Trading Time (EST)", 1)]
        public TimeSpan EndTradingTime { get; set; } = new TimeSpan(16, 0, 0);

        [InputParameter("IB Duration (Minutes)", 17, minimum: 5, maximum: 240)]
        public int IBDurationMinutes { get; set; } = 30;

        [InputParameter("Profile Step (Ticks)", 2, minimum: 1, maximum: 100)]
        public int ProfileStepTicks { get; set; } = 4;

        [InputParameter("Show Historical Profiles", 3)]
        public bool ShowHistoricalProfiles { get; set; } = false;

        [InputParameter("Label Display", 4, variants: new object[] { "None", LabelDisplayMode.None, "Label Only", LabelDisplayMode.Label, "Price Only", LabelDisplayMode.Price, "Both", LabelDisplayMode.Both })]
        public LabelDisplayMode LabelDisplay { get; set; } = LabelDisplayMode.Label;

        [InputParameter("Historical Start Date", 5)]
        public DateTime HistoricalStartDate { get; set; }

        [InputParameter("Historical End Date", 6)]
        public DateTime HistoricalEndDate { get; set; }

        [InputParameter("Show AI Insights Panel", 7)]
        public bool ShowAIInsightsBox { get; set; } = true;

        [InputParameter("Show Live Info Table", 8)]
        public bool ShowLiveInfoTable { get; set; } = true;

        [InputParameter("Show Historical Info Text", 11)]
        public bool ShowHistoricalInfo { get; set; } = true;

        [InputParameter("Show Cache Info", 12)]
        public bool ShowCacheInfo { get; set; } = true;

        [InputParameter("Generate Report", 13)]
        public bool GenerateReport { get; set; } = false;

        [InputParameter("LVN Threshold", 14, minimum: 0.01, maximum: 1.0, increment: 0.01)]
        public double LvnThreshold { get; set; } = 0.12;

        [InputParameter("HVN2 Min Ratio", 15, minimum: 0.01, maximum: 1.0, increment: 0.01)]
        public double Hvn2MinRatio { get; set; } = 0.30;

        [InputParameter("Data Aggregation Mode", 16, variants: new object[] {
            "Use Chart Default (OHLC Smearing)", 0,
            "Force True Tick Data (Accurate)", 1
        })]
        public int DataAggregationMode { get; set; } = 1;

        [InputParameter("Strategy Name", 17)]
        public string StrategyName { get; set; } = "FVP_IB_Strategy";

        [InputParameter("Enable Cognee Webhook (AI Memory)", 18)]
        public bool EnableCogneeWebhook { get; set; } = false;

        [InputParameter("Automatically Trigger Gemini AI", 19)]
        public bool AutoTriggerGemini { get; set; } = false;

        private bool isIBCalculated = false;
        private bool historyCalculated = false;
        private int fallbackRetryCount = 0;
        private DateTime lastRetryBarTime = DateTime.MinValue;
        private DateTime lastCalculatedDate = DateTime.MinValue;
        private DateTime lastSessionDate = DateTime.MinValue;
        private string currentDayStatus = "Live: Initializing...";
        private string historyStatus = "History: Initializing...";
        
        private DateTime lastServerCheckTime = DateTime.MinValue;
        private string serverLivenessStatus = "Backend: Initializing...";
        private Color serverLivenessColor = Color.Gray;

        private List<DailyIB> cachedIBs = new List<DailyIB>();
        private InitialBalanceEngine ibEngine = new InitialBalanceEngine();
        private CogneeIntegrationService cogneeService;
        private bool hasSentToCogneeToday = false;

        private string quantInsight = "Quant Insight: Waiting for 10:00 AM...";
    }
}
