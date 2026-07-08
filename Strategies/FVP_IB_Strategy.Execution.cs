using System;
using CustomStrategies.Models;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;
using CustomStrategies.Execution;
using FVP_IB_Strategy.Calculations;

namespace CustomStrategies
{
    public partial class FVP_IB_Strategy
    {
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
                            this.StrategyName, 
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
                            marketData?.IB_TotalVolume ?? double.NaN, 
                            entryPrice, 
                            marketData?.Signal?.StopLoss ?? double.NaN, 
                            marketData?.Signal?.TakeProfit ?? double.NaN,
                            marketData?.SessionHigh ?? double.NaN,
                            marketData?.SessionLow ?? double.NaN,
                            marketData?.NyOpenPrice ?? double.NaN,
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
                                // RELIABILITY FIX: 3-Second API Debounce
                                if ((DateTime.Now - lastAiRequestTime).TotalSeconds < 3) 
                                {
                                    return; // Silently skip execution on this tick to prevent API spam
                                }
                                lastAiRequestTime = DateTime.Now;

                                this.isAwaitingAiScore = true;
                                this.Log("Consulting AI memory graph for trade consensus...", StrategyLoggingLevel.Trading);
                                
                                System.Threading.Tasks.Task.Run(async () => {
                                    try 
                                    {
                                        PayloadContext ctx = new PayloadContext {
                                            StrategyName = this.StrategyName,
                                            Symbol = this.CurrentSymbol.Name,
                                            EstTime = estTime,
                                            Shape = marketData.CurrentShape.ToString(),
                                            IbHvn1 = marketData.IB_HVN1.ToString(),
                                            IbHvn2 = double.IsNaN(marketData.IB_HVN2) ? "-" : marketData.IB_HVN2.ToString(),
                                            IbLvn = double.IsNaN(marketData.IB_LVN) ? "-" : marketData.IB_LVN.ToString(),
                                            IbHigh = marketData.IB_High,
                                            IbLow = marketData.IB_Low,
                                            IbPoc = marketData.IB_POC,
                                            IbVah = marketData.IB_VAH,
                                            IbVal = marketData.IB_VAL,
                                            TotalVolume = marketData.IB_TotalVolume,
                                            EnableWebhook = this.EnableCogneeWebhook,
                                            AutoCognify = this.AutoTriggerGemini
                                        };

                                        double winProb = await this.cogneeService.AnalyzeSetupAsync(ctx);

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
                                                activeCogneePayloadsPath, this.StrategyName, this.CurrentSymbol.Name, estTime, shapeStr, "AI_VETO", "No Signal", "AI Override", ibHvn1, ibHvn2, ibLvn, marketData.IB_High, marketData.IB_Low, marketData.IB_POC, marketData.IB_VAH, marketData.IB_VAL, marketData.IB_TotalVolume, marketData.Signal.EntryPrice, marketData.Signal.StopLoss, marketData.Signal.TakeProfit, marketData.SessionHigh, marketData.SessionLow, marketData.NyOpenPrice, this.EnableCogneeWebhook, this.AutoTriggerGemini);
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
                                    catch (Exception ex)
                                    {
                                        this.Log($"CRITICAL Webhook Failure: {ex.Message}. Bypassing AI constraint to prevent bot paralysis.", StrategyLoggingLevel.Error);
                                    }
                                    finally 
                                    {
                                        this.isAwaitingAiScore = false;
                                    }
                                });
                            }
                            else
                            {
                                // Call it synchronously just to trigger the local file logging
                                PayloadContext ctx = new PayloadContext {
                                    StrategyName = this.StrategyName,
                                    Symbol = this.CurrentSymbol.Name,
                                    EstTime = estTime,
                                    Shape = marketData.CurrentShape.ToString(),
                                    IbHvn1 = marketData.IB_HVN1.ToString(),
                                    IbHvn2 = double.IsNaN(marketData.IB_HVN2) ? "-" : marketData.IB_HVN2.ToString(),
                                    IbLvn = double.IsNaN(marketData.IB_LVN) ? "-" : marketData.IB_LVN.ToString(),
                                    IbHigh = marketData.IB_High,
                                    IbLow = marketData.IB_Low,
                                    IbPoc = marketData.IB_POC,
                                    IbVah = marketData.IB_VAH,
                                    IbVal = marketData.IB_VAL,
                                    TotalVolume = marketData.IB_TotalVolume,
                                    EnableWebhook = this.EnableCogneeWebhook,
                                    AutoCognify = this.AutoTriggerGemini
                                };
                                _ = this.cogneeService.AnalyzeSetupAsync(ctx).GetAwaiter().GetResult();

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
                                this.StrategyName, this.CurrentSymbol.Name, 
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
                                marketData.SessionHigh,
                                marketData.SessionLow,
                                marketData.NyOpenPrice,
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
