using System;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;
using CustomStrategies.Execution;
using FVP_IB_Strategy.Calculations;

namespace CustomStrategies
{
    public partial class FVP_IB_Strategy
    {
        public bool IsRequirePriceLevelsCalculation => true;
        public void VolumeAnalysisData_Loaded() { }

        [InputParameter("Symbol", 0)]
        public Symbol CurrentSymbol { get; set; }

        [InputParameter("Account", 1)]
        public Account CurrentAccount { get; set; }

        [InputParameter("Start Trading Time (EST)", 2)]
        public TimeSpan StartTradingTime { get; set; } = new TimeSpan(10, 0, 0);

        [InputParameter("End Trading Time (EST)", 3)]
        public TimeSpan EndTradingTime { get; set; } = new TimeSpan(16, 0, 0);

        [InputParameter("IB Duration (Minutes)", 4, minimum: 5, maximum: 240)]
        public int IBDurationMinutes { get; set; } = 30;

        [InputParameter("Double Dist Min Ticks", 8, minimum: 1, maximum: 1000)]
        public int DoubleDistMinTicks { get; set; } = 40;

        [InputParameter("Quantity", 10, minimum: 1, maximum: 1000000)]
        public double Quantity { get; set; } = 1;

        [InputParameter("Profile Step (Ticks)", 11, minimum: 1, maximum: 100)]
        public int ProfileStepTicks { get; set; } = 4;

        [InputParameter("Timeframe Period", 12)]
        public Period Timeframe { get; set; } = Period.MIN1;

        [InputParameter("LVN Threshold", 13, minimum: 0.01, maximum: 1.0, increment: 0.01)]
        public double LvnThreshold { get; set; } = 0.12;

        [InputParameter("HVN2 Min Ratio", 14, minimum: 0.01, maximum: 1.0, increment: 0.01)]
        public double Hvn2MinRatio { get; set; } = 0.30;

        [InputParameter("Data Aggregation Mode", 15, variants: new object[] {
            "Use Chart Default (OHLC Smearing)", 0,
            "Force True Tick Data (Accurate)", 1
        })]
        public int DataAggregationMode { get; set; } = 1;

        [InputParameter("Strategy Name", 0)]
        public string StrategyName { get; set; } = "FVP_IB_Strategy";

        [InputParameter("Enable Cognee Webhook (AI Memory)", 16)]
        public bool EnableCogneeWebhook { get; set; } = false;

        [InputParameter("Automatically Trigger Gemini AI", 17)]
        public bool AutoTriggerGemini { get; set; } = false;

        public override string[] MonitoringConnectionsIds => new string[] { this.CurrentSymbol?.ConnectionId, this.CurrentAccount?.ConnectionId };

        private HistoricalData hdm;
        private Indicator atrIndicator;
        private bool historyInitialized = false;

        private MarketData marketData;
        private InitialBalanceEngine ibEngine;
        private OrderManager orderManager;
        private TradingContext tradingContext;
        private ICogneeIntegrationService cogneeService;

        private string orderTypeId;
        private string marketOrderTypeId;
        private bool waitOpenPosition;
        private bool hasTradedToday;
        private DateTime lastTradedDate = DateTime.MinValue;
        private DateTime lastSessionDate = DateTime.MinValue;

        private int totalTradesCount = 0;
        private int totalWins = 0;
        private int totalLosses = 0;
        private double totalNetProfit = 0;

        private DateTime currentSimTime = DateTime.MinValue;
        private DateTime lastOrderPlacedTime = DateTime.MinValue;
        private HashSet<string> processedPositionIds = new HashSet<string>();
        private bool isAwaitingAiScore = false;
        private DateTime lastAiRequestTime = DateTime.MinValue;

        private string activeCsvFilePath;
        private string activeAllSignalsCsvPath;
        private string activeDiagCsvPath; // Detailed per-day IB diagnostics
        private string activeCogneePayloadsPath;

        public FVP_IB_Strategy() : base()
        {
            this.Name = "FVP IB Strategy V2";
            this.Description = "Fixed Volume Profile & Initial Balance Strategy";
        }
    }
}
