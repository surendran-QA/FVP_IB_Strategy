using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;
using FVP_IB_Strategy.Calculations;

namespace CustomStrategies
{
    public partial class FVP_IB_Strategy
    {
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
                    this.StrategyName, 
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
        }
    }
}
