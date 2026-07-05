using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;
using CustomStrategies.Execution;
using FVP_IB_Strategy.Calculations;

namespace CustomStrategies
{
    public class FVP_IB_Strategy : Strategy, ICurrentAccount, ICurrentSymbol, IVolumeAnalysisIndicator
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

        private string activeCsvFilePath;
        private string activeAllSignalsCsvPath;
        private string activeDiagCsvPath; // Detailed per-day IB diagnostics
        private string activeCogneePayloadsPath;

        public FVP_IB_Strategy() : base()
        {
            this.Name = "FVP IB Strategy V2";
            this.Description = "Fixed Volume Profile & Initial Balance Strategy";
        }

        private void EnsureReportsInitialized()
        {
            if (string.IsNullOrEmpty(this.activeCsvFilePath) || string.IsNullOrEmpty(this.activeAllSignalsCsvPath))
            {
                string timestamp = Core.TimeUtils.DateTimeUtcNow.ToString("yyyyMMdd_HHmmss");
                string symbolName = this.CurrentSymbol != null ? this.CurrentSymbol.Name.Replace("/", "_").Replace(":", "_") : "SYM";

                string baseDir = global::FVP_IB_Strategy.Config.ProjectPaths.GetBaseStrategyDirectory();
                if (!System.IO.Directory.Exists(baseDir))
                {
                    System.IO.Directory.CreateDirectory(baseDir);
                }

                this.activeCsvFilePath = System.IO.Path.Combine(baseDir, $"TradeReport_Executed_trades_{timestamp}_{symbolName}.csv");
                this.activeAllSignalsCsvPath = System.IO.Path.Combine(baseDir, $"TradeReport_AllSignals_{timestamp}_{symbolName}.csv");
                this.activeDiagCsvPath = System.IO.Path.Combine(baseDir, $"IBDiagnostics_{timestamp}_{symbolName}.csv");
                this.activeCogneePayloadsPath = System.IO.Path.Combine(baseDir, $"cognee_payloads_{timestamp}_{symbolName}.txt");

                global::FVP_IB_Strategy.Calculations.ReportExporter.InitializeReport(this.activeCsvFilePath);
                global::FVP_IB_Strategy.Calculations.ReportExporter.InitializeReport(this.activeAllSignalsCsvPath);
                global::FVP_IB_Strategy.Calculations.ReportExporter.InitializeDiagReport(this.activeDiagCsvPath);
                this.Log($"[INIT] Reports created at: {baseDir}", StrategyLoggingLevel.Trading);
            }
        }

        protected override void OnRun()
        {
            this.waitOpenPosition = false; // FLAW FIX: Must initialize to false to allow the first trade to place!
            this.isAwaitingAiScore = false;
            this.totalTradesCount = 0;
            this.totalWins = 0;
            this.totalLosses = 0;
            this.totalNetProfit = 0;
            this.processedPositionIds.Clear();

            EnsureReportsInitialized();

            if (this.CurrentSymbol == null || this.CurrentAccount == null)
            {
                this.Log("Symbol or Account is not specified.", StrategyLoggingLevel.Error);
                return;
            }

            if (this.CurrentSymbol.ConnectionId != this.CurrentAccount.ConnectionId)
            {
                this.Log("Symbol and Account from different connections.", StrategyLoggingLevel.Error);
                return;
            }

            this.orderTypeId = Core.Instance.OrderTypes.FirstOrDefault(x => x.ConnectionId == this.CurrentSymbol.ConnectionId && x.Behavior == OrderTypeBehavior.Limit)?.Id;
            this.marketOrderTypeId = Core.Instance.OrderTypes.FirstOrDefault(x => x.ConnectionId == this.CurrentSymbol.ConnectionId && x.Behavior == OrderTypeBehavior.Market)?.Id;
            if (string.IsNullOrEmpty(this.orderTypeId))
            {
                this.Log("Connection does not support limit orders.", StrategyLoggingLevel.Error);
                return;
            }

            this.atrIndicator = Core.Instance.Indicators.BuiltIn.ATR(14, MaMode.SMA);

            // History loading is moved to OnUpdate to avoid the Quantower Backtester clock bug in OnRun.

            Core.PositionAdded += Core_PositionAdded;
            Core.PositionRemoved += Core_PositionRemoved;

            this.marketData = new MarketData();
            this.ibEngine = new InitialBalanceEngine();
            this.orderManager = new OrderManager();
            this.cogneeService = new CogneeIntegrationService();

            this.tradingContext = new TradingContext
            {
                CurrentAccount = this.CurrentAccount,
                CurrentSymbol = this.CurrentSymbol,
                OrderTypeId = this.orderTypeId,
                Quantity = this.Quantity,
                LogAction = (msg, level) => this.Log(msg, level),
                SetWaitOpenPosition = (val) => this.waitOpenPosition = val
            };
        }

        protected override void OnStop()
        {
            Core.PositionAdded -= Core_PositionAdded;
            Core.PositionRemoved -= Core_PositionRemoved;

            if (this.hdm != null)
            {
                this.hdm.HistoryItemUpdated -= Hdm_HistoryItemUpdated;
                this.hdm.Dispose();
            }

            if (this.cogneeService != null && this.cogneeService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }

            global::FVP_IB_Strategy.Calculations.ReportExporter.FlushReports(this.activeCsvFilePath, this.activeAllSignalsCsvPath);
            this.Log($"Successfully flushed all in-memory report rows to {this.activeCsvFilePath}!", StrategyLoggingLevel.Trading);

            base.OnStop();
        }

        private void Core_PositionAdded(Position obj)
        {
            if (obj.Symbol.Id == this.CurrentSymbol.Id && obj.Account.Id == this.CurrentAccount.Id)
                this.waitOpenPosition = false;
        }

        private void Core_PositionRemoved(Position obj)
        {
            if (obj.Symbol.Id == this.CurrentSymbol.Id && obj.Account.Id == this.CurrentAccount.Id)
            {
                EnsureReportsInitialized();

                // --- FLAW #1 FIX: Prevent duplicate CSV rows for the same position ---
                // Use OpenTime + OpenPrice as key since obj.Id differs between bracket order events
                string positionKey = $"{obj.OpenTime.Ticks}_{obj.OpenPrice}_{obj.Side}";
                if (processedPositionIds.Contains(positionKey))
                {
                    this.Log($"Skipping duplicate PositionRemoved event for position {obj.Id}", StrategyLoggingLevel.Trading);
                    return;
                }

                // --- Extract data using DOCUMENTED Quantower API ---
                double entryPrice = obj.OpenPrice;
                DateTime entryTimeUtc = obj.OpenTime;
                DateTime exitTimeUtc = this.currentSimTime != DateTime.MinValue ? this.currentSimTime : Core.TimeUtils.DateTimeUtcNow;
                
                double exitPrice = entryPrice;
                double pnl = 0;
                double pointsCaptured = 0;
                
                // --- PnL Extraction & Reconstruction (Background Mode Fallbacks) ---
                // Attempt 1: Dynamic GrossPnL
                if (pnl == 0 || double.IsNaN(pnl))
                {
                    try {
                        dynamic grossPnl = obj.GrossPnL;
                        double rawPnl = (double)grossPnl.Value;
                        if (!double.IsNaN(rawPnl) && !double.IsInfinity(rawPnl)) pnl = rawPnl;
                    } catch { }
                }

                // Attempt 3: Background Mode Reconstruction via SL / TP Sweep
                if (pnl == 0 || double.IsNaN(pnl))
                {
                    double currentHigh = 0;
                    double currentLow = 0;
                    double currentClose = 0;
                    try { 
                        var bar = (HistoryItemBar)this.hdm[0];
                        currentHigh = bar.High;
                        currentLow = bar.Low;
                        currentClose = bar.Close;
                    } catch { }
                    
                    if (currentClose > 0 && marketData != null && marketData.Signal != null && !double.IsNaN(marketData.Signal.EntryPrice))
                    {
                        double sl = marketData.Signal.StopLoss;
                        double tp = marketData.Signal.TakeProfit;
                        
                        if (obj.Side == Side.Buy)
                        {
                            if (currentLow <= sl) exitPrice = sl;
                            else if (currentHigh >= tp) exitPrice = tp;
                            else exitPrice = currentClose;
                        }
                        else 
                        {
                            if (currentHigh >= sl) exitPrice = sl;
                            else if (currentLow <= tp) exitPrice = tp;
                            else exitPrice = currentClose;
                        }
                            
                        double pts = obj.Side == Side.Buy ? (exitPrice - entryPrice) : (entryPrice - exitPrice);
                        double tVal = (obj.Symbol.TickSize * obj.Symbol.LotSize) > 0 ? (obj.Symbol.TickSize * obj.Symbol.LotSize) : 0.50; // Dynamic TickValue via LotSize
                        double tSz = obj.Symbol.TickSize > 0 ? obj.Symbol.TickSize : 0.25;
                        pnl = (pts / tSz) * tVal * obj.Quantity;
                    }
                    else if (currentClose > 0 && currentClose != entryPrice)
                    {
                        // Fallback to purely currentClose if Signal is missing
                        exitPrice = currentClose;
                        double pts = obj.Side == Side.Buy ? (exitPrice - entryPrice) : (entryPrice - exitPrice);
                        double tVal = (obj.Symbol.TickSize * obj.Symbol.LotSize) > 0 ? (obj.Symbol.TickSize * obj.Symbol.LotSize) : 0.50; // Dynamic TickValue via LotSize
                        double tSz = obj.Symbol.TickSize > 0 ? obj.Symbol.TickSize : 0.25;
                        pnl = (pts / tSz) * tVal * obj.Quantity;
                    }
                }
                
                // RECONSTRUCT EXIT PRICE if API provided PnL but no ExitPrice
                if (exitPrice == entryPrice && pnl != 0)
                {
                    double tVal = (obj.Symbol.TickSize * obj.Symbol.LotSize) > 0 ? (obj.Symbol.TickSize * obj.Symbol.LotSize) : 0.50; // Dynamic TickValue via LotSize
                    double tSz = obj.Symbol.TickSize > 0 ? obj.Symbol.TickSize : 0.25;
                    double estimatedTicks = pnl / obj.Quantity / tVal;
                    if (obj.Side == Side.Buy)
                        exitPrice = entryPrice + (estimatedTicks * tSz);
                    else
                        exitPrice = entryPrice - (estimatedTicks * tSz);
                }

                // Mark this position as processed
                processedPositionIds.Add(positionKey);
                totalTradesCount++;
                
                pointsCaptured = obj.Side == Side.Buy ? (exitPrice - entryPrice) : (entryPrice - exitPrice);

                totalNetProfit += pnl;
                if (pnl > 0) totalWins++;
                else if (pnl < 0) totalLosses++;

                // --- Convert UTC to IST for display ---
                TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                DateTime entryTimeIst = TimeZoneInfo.ConvertTimeFromUtc(entryTimeUtc, istTz);
                DateTime exitTimeIst = TimeZoneInfo.ConvertTimeFromUtc(exitTimeUtc, istTz);

                // --- Determine Status ---
                string status = "Closed";
                TimeSpan exitTod = exitTimeIst.TimeOfDay;

                bool isEodFlatten = false;
                if (EndTradingTime < StartTradingTime)
                {
                    if (exitTod >= EndTradingTime && exitTod < StartTradingTime)
                        isEodFlatten = true;
                }
                else
                {
                    if (exitTod >= EndTradingTime)
                        isEodFlatten = true;
                }

                if (isEodFlatten)
                    status = "Flattened (EOD)";
                else if (pnl > 0)
                    status = "Hit TP";
                else if (pnl < 0)
                    status = "Hit SL";

                // --- Build Result string with points ---
                string result = "";
                if (status == "Hit TP")
                    result = $"+{Math.Round(pointsCaptured, 2)} pts";
                else if (status == "Hit SL")
                    result = $"{Math.Round(pointsCaptured, 2)} pts";
                else if (status == "Flattened (EOD)")
                    result = $"{(pointsCaptured >= 0 ? "+" : "")}{Math.Round(pointsCaptured, 2)} pts";
                else
                    result = $"{Math.Round(pointsCaptured, 2)} pts";

                // --- Day of week for filtering low win-rate days ---
                string dayOfWeek = entryTimeIst.DayOfWeek.ToString();

                DateTime orderPlacedIst = this.lastOrderPlacedTime != DateTime.MinValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(this.lastOrderPlacedTime, istTz)
                    : entryTimeIst;
                string sOrderPlaced = orderPlacedIst.ToString("yyyy-MM-dd HH:mm:ss");
                string sEntryFill = entryTimeIst.ToString("yyyy-MM-dd HH:mm:ss");
                string sExitTime = exitTimeIst.ToString("yyyy-MM-dd HH:mm:ss");
                string shapeStr = marketData != null ? marketData.CurrentShape.ToString() : "Unknown";

                string ibHigh = marketData != null ? marketData.IB_High.ToString() : "-";
                string ibLow = marketData != null ? marketData.IB_Low.ToString() : "-";
                string ibPoc = marketData != null ? marketData.IB_POC.ToString() : "-";
                string ibVah = marketData != null ? marketData.IB_VAH.ToString() : "-";
                string ibVal = marketData != null ? marketData.IB_VAL.ToString() : "-";
                string ibHvn1 = marketData != null ? marketData.IB_HVN1.ToString() : "-";
                string ibHvn2 = (marketData != null && marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_HVN2)) ? marketData.IB_HVN2.ToString() : "-";
                string ibLvn = (marketData != null && marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_LVN)) ? marketData.IB_LVN.ToString() : "-";

                global::FVP_IB_Strategy.Calculations.ReportExporter.AppendReportRow(activeCsvFilePath, dayOfWeek, sOrderPlaced, sEntryFill, sExitTime, obj.Symbol.Name, obj.Side.ToString(), obj.Quantity, entryPrice.ToString(), exitPrice.ToString(), Math.Round(pnl, 2).ToString(), status, result, shapeStr, ibHigh, ibLow, ibPoc, ibVah, ibVal, ibLvn, ibHvn1, ibHvn2);
                global::FVP_IB_Strategy.Calculations.ReportExporter.AppendReportRow(activeAllSignalsCsvPath, dayOfWeek, sOrderPlaced, sEntryFill, sExitTime, obj.Symbol.Name, obj.Side.ToString(), obj.Quantity, entryPrice.ToString(), exitPrice.ToString(), Math.Round(pnl, 2).ToString(), status, result, shapeStr, ibHigh, ibLow, ibPoc, ibVah, ibVal, ibLvn, ibHvn1, ibHvn2);
                
                string execution = obj.Side.ToString() + " at " + (marketData?.Signal != null && Math.Abs(marketData.Signal.EntryPrice - marketData.IB_LVN) < obj.Symbol.TickSize*10 ? "LVN" : "POC");
                this.cogneeService?.AppendCogneePayload(
                    activeCogneePayloadsPath, 
                    obj.Symbol.Name, 
                    entryTimeIst, 
                    shapeStr, 
                    execution, 
                    status, 
                    result, 
                    ibHvn1, 
                    ibHvn2, 
                    ibLvn, 
                    marketData?.IB_High ?? double.NaN, 
                    marketData?.IB_Low ?? double.NaN, 
                    marketData?.IB_POC ?? double.NaN, 
                    marketData?.IB_VAH ?? double.NaN, 
                    marketData?.IB_VAL ?? double.NaN, 
                    marketData?.IB_TotalVolume ?? 0,
                    entryPrice, 
                    marketData?.Signal?.StopLoss ?? double.NaN, 
                    marketData?.Signal?.TakeProfit ?? double.NaN, 
                    this.EnableCogneeWebhook, 
                    this.AutoTriggerGemini);
            }
        }

        private void Hdm_HistoryItemUpdated(object sender, HistoryEventArgs e)
        {
            this.OnUpdate();
        }

        private void OnUpdate()
        {
            if (!this.historyInitialized)
            {
                // In OnUpdate, Core.TimeUtils.DateTimeUtcNow holds the proper backtest simulation clock (e.g. 6/22/2026)!
                DateTime historyFromDate = Core.TimeUtils.DateTimeUtcNow.AddDays(-5);
                this.hdm = this.CurrentSymbol.GetHistory(this.Timeframe, this.CurrentSymbol.HistoryType, historyFromDate);
                this.Log($"[INIT] History loaded: {this.hdm.Count} bars from {historyFromDate:yyyy-MM-dd} to now.", StrategyLoggingLevel.Trading);

                if (this.DataAggregationMode == 1 && this.hdm.Count > 0)
                {
                    try
                    {
                        Core.Instance.VolumeAnalysis.CalculateProfile(this.hdm);
                    }
                    catch (Exception ex)
                    {
                        this.Log($"Failed to force Volume Analysis calculation: {ex.Message}", StrategyLoggingLevel.Error);
                    }
                }

                this.hdm.AddIndicator(this.atrIndicator);
                this.hdm.HistoryItemUpdated += Hdm_HistoryItemUpdated;
                this.historyInitialized = true;
            }

            if (this.hdm.Count < 2) return;

            EnsureReportsInitialized();

            // GHOST FILTER & DST FIX: Use Core.TimeUtils.DateTimeUtcNow instead of forming bar's TimeLeft
            this.currentSimTime = Core.TimeUtils.DateTimeUtcNow;

            // TIMEZONE FIX: Use bar's CloseTime or TimeLeft - always UTC in Quantower
            // Use "Eastern Standard Time" which correctly handles EDT/EST automatically
            TimeZoneInfo estTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime estTime = TimeZoneInfo.ConvertTimeFromUtc(this.currentSimTime, estTz);
            TimeSpan currentTime = estTime.TimeOfDay;
            DateTime currentDate = estTime.Date;

            TimeSpan ibStartTime = new TimeSpan(9, 30, 0);
            TimeSpan ibEndTime = ibStartTime.Add(TimeSpan.FromMinutes(this.IBDurationMinutes));

            bool isIBPhase = currentTime >= ibStartTime && currentTime < ibEndTime;

            // FIX: Skip weekends entirely. MNQ futures resume Sunday ~6 PM ET,
            // but there is no IB on Saturday or Sunday. Without this guard the strategy
            // tries to calculate IB on Sunday night (currentDate=Sunday, buffer=last Friday).
            bool isWeekend = currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday;
            if (isWeekend) return;

            bool isExecutionPhase = StartTradingTime < EndTradingTime
                ? currentTime >= StartTradingTime && currentTime <= EndTradingTime
                : currentTime >= StartTradingTime || currentTime <= EndTradingTime;

            var currentBar = (HistoryItemBar)this.hdm[0];
            var prevBar = (HistoryItemBar)this.hdm[1];

            // --- Reset hasTradedToday on new day ---
            if (currentDate != lastTradedDate)
                hasTradedToday = false;
                
            // --- EXPLICIT SESSION RESET (FLAW #1 FIX) ---
            if (currentDate != lastSessionDate)
            {
                this.marketData = new MarketData(); // Completely wipe the structs/arrays
                this.ibEngine = new InitialBalanceEngine(); // Re-instantiate the engine to clear any internal buffers
                this.lastSessionDate = currentDate;
                this.Log($"[RESET] Session explicitly wiped for {currentDate:yyyy-MM-dd}", StrategyLoggingLevel.Trading);
            }

            if (isIBPhase)
            {
                marketData.IsIBCalculated = false;
                marketData.CurrentShape = VolumeProfileShape.Unknown;
                return; // Don't try to trade during IB phase
            }

            if (currentTime >= ibEndTime && (!marketData.IsIBCalculated || marketData.LastCalculatedDate != currentDate))
            {
                this.Log($"[IB] Attempting IB for {currentDate:yyyy-MM-dd} using historical data scan...", StrategyLoggingLevel.Trading);
                
                // FORCE VolumeAnalysis update for the newly streamed bars before calculation
                if (this.DataAggregationMode == 1)
                {
                    try { Core.Instance.VolumeAnalysis.CalculateProfile(this.hdm); }
                    catch { }
                }

                if (ibEngine.CalculateIB(this.hdm, this.CurrentSymbol, estTime, this.IBDurationMinutes, this.ProfileStepTicks, this.DoubleDistMinTicks, out marketData, out bool isPrecise, this.LvnThreshold, this.Hvn2MinRatio))
                {
                    string mode = isPrecise ? "Tick" : "Fallback";
                    this.Log($"IB OK ({mode}) [{currentDate:yyyy-MM-dd}]: H={marketData.IB_High} L={marketData.IB_Low} POC={marketData.IB_POC} VAH={marketData.IB_VAH} VAL={marketData.IB_VAL} Shape={marketData.CurrentShape} Signal={marketData.Signal?.PreferredSide}", StrategyLoggingLevel.Trading);
                    // Write detailed diagnostic row
                    global::FVP_IB_Strategy.Calculations.ReportExporter.AppendDiagRow(
                        this.activeDiagCsvPath, currentDate.ToString("yyyy-MM-dd"),
                        currentDate.DayOfWeek.ToString(), 30, isPrecise,
                        marketData.IB_High, marketData.IB_Low, marketData.IB_POC,
                        marketData.IB_VAH, marketData.IB_VAL, marketData.CurrentShape.ToString(),
                        marketData.Signal?.PreferredSide ?? "NONE",
                        marketData.Signal?.EntryPrice ?? double.NaN,
                        marketData.Signal?.StopLoss ?? double.NaN,
                        marketData.Signal?.TakeProfit ?? double.NaN);
                }
                else
                {
                    this.Log($"[IB] Calc FAILED for {currentDate:yyyy-MM-dd}. No IB bars found or ProfileCalc failed.", StrategyLoggingLevel.Error);
                }
            }
            if (isExecutionPhase && marketData.IsIBCalculated)
            {
                var positions = Core.Instance.Positions.Where(x => x.Symbol.Id == this.CurrentSymbol.Id && x.Account.Id == this.CurrentAccount.Id).ToArray();
                var pendingOrders = Core.Instance.Orders.Where(x => x.Symbol.Id == this.CurrentSymbol.Id && x.Account.Id == this.CurrentAccount.Id && (x.Status == OrderStatus.Opened || x.Status == OrderStatus.PartiallyFilled)).ToArray();

                // --- SELF-HEALING STATE MANAGEMENT (Prevents waitOpenPosition deadlock) ---
                if (waitOpenPosition && !positions.Any() && !pendingOrders.Any())
                {
                    this.waitOpenPosition = false;
                }
            } // END OF isExecutionPhase block

            // --- FLAW FIX: EOD Flattening must be evaluated OUTSIDE isExecutionPhase ---
            // Because isExecutionPhase requires currentTime <= EndTradingTime, 
            // any check for currentTime > EndTradingTime inside it would be a logical paradox and never run!
            if (marketData.IsIBCalculated)
            {
                var positions = Core.Instance.Positions.Where(x => x.Symbol.Id == this.CurrentSymbol.Id && x.Account.Id == this.CurrentAccount.Id).ToArray();
                var pendingOrders = Core.Instance.Orders.Where(x => x.Symbol.Id == this.CurrentSymbol.Id && x.Account.Id == this.CurrentAccount.Id && (x.Status == OrderStatus.Opened || x.Status == OrderStatus.PartiallyFilled)).ToArray();

                // End Of Day Flattening
                if (currentTime > EndTradingTime && EndTradingTime > StartTradingTime ||
                    (EndTradingTime < StartTradingTime && currentTime > EndTradingTime && currentTime < StartTradingTime))
                {
                    if (positions.Any())
                    {
                        var pos = positions.First();
                        // --- FLAW #5 FIX: Use Market order for EOD flatten to guarantee fill ---
                        string eodOrderType = this.marketOrderTypeId ?? this.orderTypeId;
                        var closeReq = new PlaceOrderRequestParameters()
                        {
                            Account = this.CurrentAccount,
                            Symbol = this.CurrentSymbol,
                            OrderTypeId = eodOrderType,
                            Quantity = pos.Quantity,
                            Side = pos.Side == Side.Buy ? Side.Sell : Side.Buy
                        };
                        Core.Instance.PlaceOrder(closeReq);
                        this.Log($"EOD Reached. Flattening Open Position: {closeReq.Side} {closeReq.Quantity}", StrategyLoggingLevel.Trading);
                    }

                    foreach (var order in pendingOrders)
                    {
                        Core.Instance.CancelOrder(new CancelOrderRequestParameters { OrderId = order.Id });
                        this.Log($"EOD Reached. Cancelling pending order {order.Id}", StrategyLoggingLevel.Trading);
                        this.waitOpenPosition = false; // FLAW FIX: Unblock strategy for the next day's session

                        // LOG UNEXECUTED/PENDING ORDER TO CSV
                        TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                        DateTime orderPlacedIst = this.lastOrderPlacedTime != DateTime.MinValue
                            ? TimeZoneInfo.ConvertTimeFromUtc(this.lastOrderPlacedTime, istTz)
                            : estTime;
                        string sOrderPlaced = orderPlacedIst.ToString("yyyy-MM-dd HH:mm:ss");
                        string dayOfWeek = orderPlacedIst.DayOfWeek.ToString();
                        string shapeStr = marketData != null ? marketData.CurrentShape.ToString() : "Unknown";
                        string ibHigh = marketData != null ? marketData.IB_High.ToString() : "-";
                        string ibLow = marketData != null ? marketData.IB_Low.ToString() : "-";
                        string ibPoc = marketData != null ? marketData.IB_POC.ToString() : "-";
                        string ibVah = marketData != null ? marketData.IB_VAH.ToString() : "-";
                        string ibVal = marketData != null ? marketData.IB_VAL.ToString() : "-";
                        string ibHvn1 = marketData != null ? marketData.IB_HVN1.ToString() : "-";
                        string ibHvn2 = (marketData != null && marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_HVN2)) ? marketData.IB_HVN2.ToString() : "-";
                        string ibLvn = (marketData != null && marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_LVN)) ? marketData.IB_LVN.ToString() : "-";
                        double entryPrice = marketData != null && marketData.Signal != null ? marketData.Signal.EntryPrice : order.Price;

                        global::FVP_IB_Strategy.Calculations.ReportExporter.AppendReportRow(activeCsvFilePath, dayOfWeek, sOrderPlaced, "-", "-", this.CurrentSymbol.Name, order.Side.ToString(), order.TotalQuantity, entryPrice.ToString(), "-", "0", "Pending (Not Triggered)", "0 pts", shapeStr, ibHigh, ibLow, ibPoc, ibVah, ibVal, ibLvn, ibHvn1, ibHvn2);
                        global::FVP_IB_Strategy.Calculations.ReportExporter.AppendReportRow(activeAllSignalsCsvPath, dayOfWeek, sOrderPlaced, "-", "-", this.CurrentSymbol.Name, order.Side.ToString(), order.TotalQuantity, entryPrice.ToString(), "-", "0", "Pending (Not Triggered)", "0 pts", shapeStr, ibHigh, ibLow, ibPoc, ibVah, ibVal, ibLvn, ibHvn1, ibHvn2);
                        
                        this.cogneeService?.AppendCogneePayload(
                            activeCogneePayloadsPath, 
                            this.CurrentSymbol.Name, 
                            orderPlacedIst, 
                            shapeStr, 
                            "NONE", 
                            "Cancelled", 
                            "EOD Reached", 
                            ibHvn1, 
                            ibHvn2, 
                            ibLvn, 
                            marketData?.IB_High ?? double.NaN, 
                            marketData?.IB_Low ?? double.NaN, 
                            marketData?.IB_POC ?? double.NaN, 
                            marketData?.IB_VAH ?? double.NaN, 
                            marketData?.IB_VAL ?? double.NaN, 
                            marketData?.IB_TotalVolume ?? 0,
                            entryPrice, 
                            marketData?.Signal?.StopLoss ?? double.NaN, 
                            marketData?.Signal?.TakeProfit ?? double.NaN, 
                            this.EnableCogneeWebhook, 
                            this.AutoTriggerGemini);
                    }
                    return;
                }

                if (positions.Any() || waitOpenPosition || hasTradedToday || isAwaitingAiScore) return;

                // Place Limit Order when IB is calculated and execution phase begins
                if (currentTime >= StartTradingTime)
                {
                    if (marketData.Signal != null)
                    {
                        if (marketData.Signal.PreferredSide != "FADE")
                        {
                            if (this.EnableCogneeWebhook)
                            {
                                this.isAwaitingAiScore = true;
                                this.Log("Consulting AI memory graph for trade consensus...", StrategyLoggingLevel.Trading);
                                
                                System.Threading.Tasks.Task.Run(async () => {
                                    try 
                                    {
                                        string response = await this.cogneeService.AnalyzeSetupAsync(
                                            this.CurrentSymbol.Name, estTime, marketData.CurrentShape.ToString(), 
                                            marketData.IB_HVN1.ToString(), 
                                            double.IsNaN(marketData.IB_HVN2) ? "-" : marketData.IB_HVN2.ToString(), 
                                            double.IsNaN(marketData.IB_LVN) ? "-" : marketData.IB_LVN.ToString(), 
                                            marketData.IB_High, marketData.IB_Low, marketData.IB_POC, 
                                            marketData.IB_VAH, marketData.IB_VAL, marketData.IB_TotalVolume, 
                                            this.EnableCogneeWebhook
                                        );
                                        
                                        // Strictly-Typed JSON Parsing (ARCH-01 Compliance)
                                        double winProb = 50; // Fallback
                                        try 
                                        {
                                            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(response))
                                            {
                                                if (doc.RootElement.TryGetProperty("win_probability", out var probElement))
                                                {
                                                    string probStr = probElement.GetString()?.Replace("%", "").Trim();
                                                    if (double.TryParse(probStr, out double parsedProb))
                                                    {
                                                        winProb = parsedProb;
                                                    }
                                                }
                                            }
                                        } 
                                        catch (System.Text.Json.JsonException jsonEx) 
                                        { 
                                            this.Log($"AI JSON Parse Error: {jsonEx.Message}. Using baseline 50%.", StrategyLoggingLevel.Error);
                                        }

                                        if (winProb < 40)
                                        {
                                            this.Log($"AI VETO (Prob: {winProb}%): Mathematical setup overridden due to poor historical context. Logging FADE.", StrategyLoggingLevel.Trading);
                                            
                                            // Act as a FADE day and log it for ML
                                            this.lastTradedDate = currentDate;
                                            string dayOfWeek = estTime.DayOfWeek.ToString();
                                            string sOrderPlaced = estTime.ToString("yyyy-MM-dd HH:mm:ss");
                                            string shapeStr = marketData.CurrentShape.ToString();
                                            string ibHigh = marketData.IB_High.ToString();
                                            string ibLow = marketData.IB_Low.ToString();
                                            string ibPoc = marketData.IB_POC.ToString();
                                            string ibVah = marketData.IB_VAH.ToString();
                                            string ibVal = marketData.IB_VAL.ToString();
                                            string ibHvn1 = marketData.IB_HVN1.ToString();
                                            string ibHvn2 = (marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_HVN2)) ? marketData.IB_HVN2.ToString() : "-";
                                            string ibLvn = (marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_LVN)) ? marketData.IB_LVN.ToString() : "-";

                                            global::FVP_IB_Strategy.Calculations.ReportExporter.AppendReportRow(activeAllSignalsCsvPath, dayOfWeek, sOrderPlaced, "-", "-", this.CurrentSymbol.Name, "AI_VETO", 1, "-", "-", "0", "AI Vetoed", "0 pts", shapeStr, ibHigh, ibLow, ibPoc, ibVah, ibVal, ibLvn, ibHvn1, ibHvn2);
                                            
                                            // Write to Cognee Webhook too
                                            this.cogneeService?.AppendCogneePayload(
                                                activeCogneePayloadsPath, this.CurrentSymbol.Name, estTime, shapeStr, "AI_VETO", "No Signal", "AI Override", ibHvn1, ibHvn2, ibLvn, marketData.IB_High, marketData.IB_Low, marketData.IB_POC, marketData.IB_VAH, marketData.IB_VAL, marketData.IB_TotalVolume, marketData.Signal.EntryPrice, marketData.Signal.StopLoss, marketData.Signal.TakeProfit, this.EnableCogneeWebhook, this.AutoTriggerGemini);
                                        }
                                        else
                                        {
                                            this.Log($"AI APPROVAL (Prob: {winProb}%): Placing mathematical order.", StrategyLoggingLevel.Trading);
                                            this.lastOrderPlacedTime = this.currentSimTime;
                                            this.lastTradedDate = currentDate;
                                            orderManager.ExecuteTrade(marketData, tradingContext);
                                        }
                                        hasTradedToday = true;
                                    }
                                    finally 
                                    {
                                        this.isAwaitingAiScore = false;
                                    }
                                });
                            }
                            else
                            {
                                this.lastOrderPlacedTime = this.currentSimTime; // Capture order placement time
                                this.lastTradedDate = currentDate; // Track which date we traded
                                this.Log($"Triggering Trade Execution: Side={marketData.Signal.PreferredSide}, Entry={marketData.Signal.EntryPrice}", StrategyLoggingLevel.Trading);
                                orderManager.ExecuteTrade(marketData, tradingContext);
                                hasTradedToday = true;
                            }
                        }
                        else
                        {
                            // --- FADE / NO ORDER DAY LOGGING FOR MACHINE LEARNING ---
                            this.lastTradedDate = currentDate;
                            string dayOfWeek = estTime.DayOfWeek.ToString();
                            string sOrderPlaced = estTime.ToString("yyyy-MM-dd HH:mm:ss");
                            string shapeStr = marketData.CurrentShape.ToString();
                            string ibHigh = marketData.IB_High.ToString();
                            string ibLow = marketData.IB_Low.ToString();
                            string ibPoc = marketData.IB_POC.ToString();
                            string ibVah = marketData.IB_VAH.ToString();
                            string ibVal = marketData.IB_VAL.ToString();
                            string ibHvn1 = marketData.IB_HVN1.ToString();
                            string ibHvn2 = (marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_HVN2)) ? marketData.IB_HVN2.ToString() : "-";
                            string ibLvn = (marketData.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(marketData.IB_LVN)) ? marketData.IB_LVN.ToString() : "-";

                            this.Log($"FADE Signal Generated (No Trade Day). Logging to AllSignals CSV for ML Training.", StrategyLoggingLevel.Trading);
                            global::FVP_IB_Strategy.Calculations.ReportExporter.AppendReportRow(activeAllSignalsCsvPath, dayOfWeek, sOrderPlaced, "-", "-", this.CurrentSymbol.Name, "FADE", 1, "-", "-", "0", "No Signal", "0 pts", shapeStr, ibHigh, ibLow, ibPoc, ibVah, ibVal, ibLvn, ibHvn1, ibHvn2);
                            
                            this.cogneeService?.AppendCogneePayload(
                                activeCogneePayloadsPath, 
                                this.CurrentSymbol.Name, 
                                estTime, 
                                shapeStr, 
                                "FADE", 
                                "No Signal", 
                                "No Trade Day", 
                                ibHvn1, 
                                ibHvn2, 
                                ibLvn, 
                                marketData.IB_High, 
                                marketData.IB_Low, 
                                marketData.IB_POC, 
                                marketData.IB_VAH, 
                                marketData.IB_VAL, 
                                marketData.IB_TotalVolume,
                                marketData.Signal.EntryPrice, 
                                marketData.Signal.StopLoss, 
                                marketData.Signal.TakeProfit, 
                                this.EnableCogneeWebhook, 
                                this.AutoTriggerGemini);
                            hasTradedToday = true; // Prevents logging multiple times per day
                        }
                    }
                }
            }
        }
    }
}


