using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;
using FVP_IB_Strategy.Calculations;

namespace CustomStrategies
{
    public enum LabelDisplayMode
    {
        None,
        Label,
        Price,
        Both
    }

    public class DailyIB
    {
        public DateTime ProfileStartUtc;
        public DateTime ExecutionStartUtc;
        public DateTime ExecutionEndUtc;
        public double VAH;
        public double VAL;
        public double POC;
        public double High;
        public double Low;
        public double HVN2;
        public double LVN;
        public double TotalVolume;
        public bool IsPrecise;
        public VolumeProfileShape CurrentShape;
        public Calculations.TradeSignal Signal;
        public bool IsHistorical;
    }

    public partial class IBVisualizerIndicator : Indicator, IVolumeAnalysisIndicator
    {
        public bool IsRequirePriceLevelsCalculation => true;
        public void VolumeAnalysisData_Loaded() { }

        public IBVisualizerIndicator()
        {
            Name = "FVP IB Indicator V2";
            Description = "Visualizes FVP IB Phase mathematically";
            this.SeparateWindow = false;
            this.HistoricalEndDate = DateTime.Today.AddDays(-1);
            this.HistoricalStartDate = DateTime.Today.AddDays(-6);
        }

        protected override void OnInit()
        {
            this.cogneeService = new CogneeIntegrationService();

            if (this.DataAggregationMode == 1)
            {
                try { Core.Instance.VolumeAnalysis.CalculateProfile(this.HistoricalData); }
                catch (Exception ex) { Core.Instance.Loggers.Log($"Failed to force Volume Analysis: {ex.Message}"); }
            }
            isIBCalculated = false;
            historyCalculated = false;
            lastCalculatedDate = DateTime.MinValue;
            cachedIBs.Clear();
            currentDayStatus = "Live: Initializing...";
            historyStatus = "History: Initializing...";
            quantInsight = "Quant Insight: Waiting for 10:00 AM...";
            base.OnInit();
        }

        protected override void OnClear()
        {
            if (this.cogneeService != null && this.cogneeService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }
            base.OnClear();
        }

        protected override void OnUpdate(UpdateArgs args)
        {
            if (this.HistoricalData.Count < 2) return;

            var currentBar = (HistoryItemBar)this.HistoricalData[0];
            DateTime istTime = TimeZoneInfo.ConvertTimeFromUtc(currentBar.TimeLeft, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            TimeSpan currentTime = istTime.TimeOfDay;

            TimeSpan ibStartTime = new TimeSpan(9, 30, 0);
            TimeSpan ibEndTime = ibStartTime.Add(TimeSpan.FromMinutes(this.IBDurationMinutes));
            bool isIBPhase = currentTime >= ibStartTime && currentTime < ibEndTime;

            // --- EXPLICIT SESSION RESET (FLAW #1 FIX) ---
            if (istTime.Date != lastSessionDate)
            {
                this.ibEngine = new InitialBalanceEngine(); // Re-instantiate the engine to clear any internal buffers
                this.lastSessionDate = istTime.Date;
                // Note: cachedIBs shouldn't be wiped here because they are drawn on the chart. 
                // Only wipe the engine calculating the NEW IB.
            }

            if (isIBPhase)
            {
                isIBCalculated = false;
                currentDayStatus = "Waiting for IB phase to finish";
                return;
            }

            if (currentTime >= ibEndTime && (!isIBCalculated || lastCalculatedDate != istTime.Date))
            {
                if (lastCalculatedDate != istTime.Date)
                {
                    hasSentToCogneeToday = false;
                }

                if (ibEngine.CalculateIB(this.HistoricalData, this.Symbol, istTime, this.IBDurationMinutes, ProfileStepTicks, 40, out MarketData md, out bool isPrecise, this.LvnThreshold, this.Hvn2MinRatio))
                {
                    TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    ExecutionSimulator sim = new ExecutionSimulator();
                    sim.SimulateExecution(md, this.HistoricalData, ibEndTime, EndTradingTime, istTz);

                    CacheIB(md, istTime.Date, isPrecise, false);
                    currentDayStatus = "Live: " + (isPrecise ? "Precise" : "Fallback");

                    isIBCalculated = true;
                    lastCalculatedDate = istTime.Date;

                    // FIRE WEBHOOK TO COGNEE (Phase 2.1)
                    if (!hasSentToCogneeToday)
                    {
                        hasSentToCogneeToday = true;

                        quantInsight = "Quant Insight: Analyzing...";

                        Task.Run(async () =>
                        {
                            if (this.cogneeService != null)
                            {
                                string responseStr = await this.cogneeService.AnalyzeSetupAsync(
                                    this.StrategyName,
                                    this.Symbol.Name,
                                    istTime,
                                    md.CurrentShape.ToString(),
                                    md.IB_HVN1.ToString(),
                                    double.IsNaN(md.IB_HVN2) ? "-" : md.IB_HVN2.ToString(),
                                    double.IsNaN(md.IB_LVN) ? "-" : md.IB_LVN.ToString(),
                                    md.IB_High,
                                    md.IB_Low,
                                    md.IB_POC,
                                    md.IB_VAH,
                                    md.IB_VAL,
                                    md.IB_TotalVolume,
                                    this.EnableCogneeWebhook);

                                string parsedScore = "50";
                                string parsedProb = "50%";
                                try
                                {
                                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(responseStr))
                                    {
                                        if (doc.RootElement.TryGetProperty("confidence_score", out var scoreElement))
                                            parsedScore = scoreElement.GetString() ?? "50";
                                        if (doc.RootElement.TryGetProperty("historical_win_rate", out var probElement))
                                            parsedProb = probElement.GetString() ?? "50%";
                                    }
                                    this.quantInsight = $"Quant Insight: Score {parsedScore} | Probability {parsedProb}";
                                }
                                catch
                                {
                                    this.quantInsight = "Quant Insight: JSON Parse Error";
                                }
                            }
                        });
                    }
                }
            }

            // CONTINUOUS SIMULATION UPDATE FOR ALL OPEN SIGNALS (LIVE & REPLAY STREAMING)
            TimeSpan ibStartTimeCont = new TimeSpan(9, 30, 0);
            TimeSpan ibEndTimeCont = ibStartTimeCont.Add(TimeSpan.FromMinutes(this.IBDurationMinutes));
            ExecutionSimulator continuousSim = new ExecutionSimulator();
            TimeZoneInfo istTzCont = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            foreach (var ib in cachedIBs.Where(i => i.Signal != null && (i.Signal.Status == "Waiting" || i.Signal.Status == "In Trade")))
            {
                MarketData dummyMd = new MarketData();
                dummyMd.Signal = ib.Signal;
                dummyMd.LastCalculatedDate = TimeZoneInfo.ConvertTimeFromUtc(ib.ExecutionStartUtc, istTzCont).Date;

                continuousSim.SimulateExecution(dummyMd, this.HistoricalData, ibEndTimeCont, EndTradingTime, istTzCont);

                if (ib.Signal.Status == "Closed" && !ib.Signal.IsMemoryPayloadSent)
                {
                    ib.Signal.IsMemoryPayloadSent = true;
                    string execution = ib.Signal.PreferredSide + " at " + (Math.Abs(ib.Signal.EntryPrice - ib.LVN) < 2.5 ? "LVN" : "POC");
                    double exitPrice = ib.Signal.ExitReason == "TP Hit" ? ib.Signal.TakeProfit : ib.Signal.StopLoss;
                    double pnl = (ib.Signal.PreferredSide == "BUY" || ib.Signal.PreferredSide == "LONG") ? (exitPrice - ib.Signal.EntryPrice) : (ib.Signal.EntryPrice - exitPrice);
                    string status = ib.Signal.ExitReason ?? "Closed";
                    string result = $"{(pnl > 0 ? "+" : "")}{Math.Round(pnl, 2)} pts";

                    string logPath = global::FVP_IB_Strategy.Config.ProjectPaths.GetLogFilePath();
                    this.cogneeService?.AppendCogneePayload(
                        logPath,
                        this.StrategyName,
                        this.Symbol.Name,
                        ib.Signal.EntryTime ?? DateTime.UtcNow,
                        ib.CurrentShape.ToString(),
                        execution,
                        status,
                        result,
                        ib.POC.ToString(),
                        double.IsNaN(ib.HVN2) ? "-" : ib.HVN2.ToString(),
                        double.IsNaN(ib.LVN) ? "-" : ib.LVN.ToString(),
                        ib.High,
                        ib.Low,
                        ib.POC,
                        ib.VAH,
                        ib.VAL,
                        ib.TotalVolume, // IB_TotalVolume
                        ib.Signal.EntryPrice,
                        ib.Signal.StopLoss,
                        ib.Signal.TakeProfit,
                        this.EnableCogneeWebhook,
                        this.AutoTriggerGemini
                    );
                }
            }
        }

        private void CalculateAllHistoricalIBs()
        {
            HashSet<DateTime> processedDates = new HashSet<DateTime>();
            TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            for (int i = this.HistoricalData.Count - 1; i >= 0; i--)
            {
                var bar = (HistoryItemBar)this.HistoricalData[i];
                DateTime barIst = TimeZoneInfo.ConvertTimeFromUtc(bar.TimeLeft, istTz);
                DateTime currentSimDate = barIst.Date;

                if (currentSimDate == DateTime.Today) continue;

                if (currentSimDate >= HistoricalStartDate.Date && currentSimDate <= HistoricalEndDate.Date)
                {
                    if (!processedDates.Contains(currentSimDate))
                    {
                        TimeSpan ibStartTime = new TimeSpan(9, 30, 0);
                        TimeSpan ibEndTime = ibStartTime.Add(TimeSpan.FromMinutes(this.IBDurationMinutes));
                        if (barIst.TimeOfDay >= ibEndTime)
                        {
                            if (ibEngine.CalculateIB(this.HistoricalData, this.Symbol, barIst, this.IBDurationMinutes, ProfileStepTicks, 40, out MarketData md, out bool isPrecise, this.LvnThreshold, this.Hvn2MinRatio))
                            {
                                ExecutionSimulator sim = new ExecutionSimulator();
                                sim.SimulateExecution(md, this.HistoricalData, ibEndTime, EndTradingTime, istTz);

                                processedDates.Add(currentSimDate);
                                CacheIB(md, currentSimDate, isPrecise, true);
                            }
                        }
                    }
                }
            }
        }

        private void CacheIB(MarketData md, DateTime currentSimDate, bool isPrecise, bool isHistorical)
        {
            TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime profileStartIst = currentSimDate.Add(new TimeSpan(9, 30, 0));
            DateTime execStartIst = currentSimDate.Add(new TimeSpan(9, 30, 0)).AddMinutes(this.IBDurationMinutes);

            DateTime sessionEndIst;
            if (EndTradingTime < StartTradingTime)
                sessionEndIst = currentSimDate.AddDays(1).Add(EndTradingTime);
            else
                sessionEndIst = currentSimDate.Add(EndTradingTime);

            DateTime execStartUtc = TimeZoneInfo.ConvertTimeToUtc(execStartIst, istTz);

            cachedIBs.RemoveAll(x => x.ExecutionStartUtc.Date == execStartUtc.Date);

            cachedIBs.Add(new DailyIB
            {
                ProfileStartUtc = TimeZoneInfo.ConvertTimeToUtc(profileStartIst, istTz),
                ExecutionStartUtc = execStartUtc,
                ExecutionEndUtc = TimeZoneInfo.ConvertTimeToUtc(sessionEndIst, istTz),
                VAH = md.IB_VAH,
                VAL = md.IB_VAL,
                POC = md.IB_POC,
                High = md.IB_High,
                Low = md.IB_Low,
                HVN2 = md.IB_HVN2,
                LVN = md.IB_LVN,
                TotalVolume = md.IB_TotalVolume,
                IsPrecise = isPrecise,
                CurrentShape = md.CurrentShape,
                Signal = md.Signal,
                IsHistorical = isHistorical
            });

            if (isHistorical)
                historyStatus = $"History: {cachedIBs.Count} IBs";
            else
                currentDayStatus = $"Live: {(isPrecise ? "Precise" : "Fallback")}";
        }



        private void GenerateIndicatorReport()
        {
            try
            {
                string csvFilePath = System.IO.Path.Combine(global::FVP_IB_Strategy.Config.ProjectPaths.GetBaseStrategyDirectory(), "IndicatorReport.csv");
                global::FVP_IB_Strategy.Calculations.ReportExporter.InitializeReport(csvFilePath);

                // Sort cached IBs by date ascending
                var sortedIBs = cachedIBs.OrderBy(x => x.ExecutionStartUtc).ToList();

                foreach (var ib in sortedIBs)
                {
                    AppendSingleIBToReport(ib, csvFilePath);
                }
                historyStatus += " [Report Generated]";
            }
            catch (Exception ex)
            {
                historyStatus += $" [Report Failed: {ex.Message}]";
            }
        }

        private void AppendSingleIBToReport(DailyIB ib, string csvFilePath)
        {
            TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime execStartIst = TimeZoneInfo.ConvertTimeFromUtc(ib.ExecutionStartUtc, istTz);
            string dayOfWeek = execStartIst.DayOfWeek.ToString();
            string sOrderPlaced = execStartIst.ToString("yyyy-MM-dd HH:mm:ss");

            string sEntryFill = "-";
            string sExitTime = "-";
            string sideStr = "-";
            string entryPriceStr = "-";
            string exitPriceStr = "-";
            string pnlStr = "0";
            string status = "No Signal";
            string result = "0 pts";

            if (ib.Signal != null && ib.CurrentShape != VolumeProfileShape.Unknown)
            {
                sideStr = ib.Signal.PreferredSide;
                entryPriceStr = ib.Signal.EntryPrice.ToString();
                status = ib.Signal.Status;

                if (ib.Signal.EntryTime.HasValue)
                {
                    sEntryFill = ib.Signal.EntryTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                }

                if (ib.Signal.ExitTime.HasValue)
                {
                    sExitTime = ib.Signal.ExitTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                }

                if (status == "Closed" && !string.IsNullOrEmpty(ib.Signal.ExitReason))
                {
                    status = ib.Signal.ExitReason;
                }

                // Calculate points & pnl based on signal simulation
                double points = 0;
                if (ib.Signal.Status == "Closed" && ib.Signal.EntryTime.HasValue)
                {
                    double exitPrice = ib.Signal.ExitReason == "TP Hit" ? ib.Signal.TakeProfit : ib.Signal.StopLoss;
                    exitPriceStr = exitPrice.ToString();
                    points = (ib.Signal.PreferredSide == "BUY" || ib.Signal.PreferredSide == "LONG") ? (exitPrice - ib.Signal.EntryPrice) : (ib.Signal.EntryPrice - exitPrice);
                    double pnl = points * (this.Symbol != null ? (this.Symbol.TickSize > 0 ? (1.0 / this.Symbol.TickSize) * 0.5 : 1) : 1); // rough estimation for MNQ or general points
                    pnlStr = Math.Round(pnl, 2).ToString();
                    result = $"{(points > 0 ? "+" : "")}{Math.Round(points, 2)} pts";
                }
                else if (ib.Signal.Status == "Pending")
                {
                    status = "Pending (Not Triggered)";
                    result = "0 pts";
                }
            }

            string shapeStr = ib.CurrentShape.ToString();
            string ibHigh = ib.High.ToString();
            string ibLow = ib.Low.ToString();
            string ibPoc = ib.POC.ToString();
            string ibVah = ib.VAH.ToString();
            string ibVal = ib.VAL.ToString();
            string ibHvn1 = ib.POC.ToString();
            string ibHvn2 = (ib.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(ib.HVN2)) ? ib.HVN2.ToString() : "-";
            string ibLvn = (ib.CurrentShape == VolumeProfileShape.BShape && !double.IsNaN(ib.LVN)) ? ib.LVN.ToString() : "-";

            global::FVP_IB_Strategy.Calculations.ReportExporter.AppendReportRow(csvFilePath, dayOfWeek, sOrderPlaced, sEntryFill, sExitTime, (this.Symbol != null ? this.Symbol.Name : "MNQU26"), sideStr, 1, entryPriceStr, exitPriceStr, pnlStr, status, result, shapeStr, ibHigh, ibLow, ibPoc, ibVah, ibVal, ibLvn, ibHvn1, ibHvn2);
        }
    }
}

