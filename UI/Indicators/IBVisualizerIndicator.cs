using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;

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
        public bool IsPrecise;
        public VolumeProfileShape CurrentShape;
        public Calculations.TradeSignal Signal;
        public bool IsHistorical;
    }

    public class IBVisualizerIndicator : Indicator, IVolumeAnalysisIndicator
    {
        public bool IsRequirePriceLevelsCalculation => true;
        public void VolumeAnalysisData_Loaded() { }

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

        [InputParameter("Show Live Info Table", 7)]
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

        private bool isIBCalculated = false;
        private bool historyCalculated = false;
        private DateTime lastCalculatedDate = DateTime.MinValue;
        private string currentDayStatus = "Live: Initializing...";
        private string historyStatus = "History: Initializing...";
        
        private List<DailyIB> cachedIBs = new List<DailyIB>();
        private InitialBalanceEngine ibEngine = new InitialBalanceEngine();

        public IBVisualizerIndicator()
        {
            Name = "FVP IB Indicator V1.1";
            Description = "Visualizes FVP IB Phase mathematically";
            this.SeparateWindow = false;
            this.HistoricalEndDate = DateTime.Today.AddDays(-1);
            this.HistoricalStartDate = DateTime.Today.AddDays(-6);
        }

        protected override void OnInit()
        {
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
            base.OnInit();
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

            if (isIBPhase)
            {
                isIBCalculated = false;
                currentDayStatus = "Waiting for IB phase to finish";
                return;
            }

            if (currentTime >= ibEndTime && (!isIBCalculated || lastCalculatedDate != istTime.Date))
            {
                if (ibEngine.CalculateIB(this.HistoricalData, this.Symbol, istTime, this.IBDurationMinutes, ProfileStepTicks, 40, out MarketData md, out bool isPrecise, this.LvnThreshold, this.Hvn2MinRatio))
                {
                    TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    ExecutionSimulator sim = new ExecutionSimulator();
                    sim.SimulateExecution(md, this.HistoricalData, ibEndTime, EndTradingTime, istTz);

                    CacheIB(md, istTime.Date, isPrecise, false);
                    currentDayStatus = "Live: " + (isPrecise ? "Precise" : "Fallback");
                    
                    isIBCalculated = true;
                    lastCalculatedDate = istTime.Date;
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

            cachedIBs.Add(new DailyIB {
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

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);

            if (this.HistoricalData != null && this.HistoricalData.Count > 0)
            {
                if ((ShowHistoricalProfiles || GenerateReport) && !historyCalculated)
                {
                    historyStatus = "History: Calculating Historical Profiles...";
                    CalculateAllHistoricalIBs();
                    historyCalculated = true;
                    if (GenerateReport)
                    {
                        GenerateIndicatorReport();
                    }
                }

                var currentBar = (HistoryItemBar)this.HistoricalData[0];
                DateTime istTime = TimeZoneInfo.ConvertTimeFromUtc(currentBar.TimeLeft, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
                
                // CLEANUP: Mark old live IBs as historical when day rolls over
                foreach (var ib in cachedIBs.Where(i => !i.IsHistorical).ToList())
                {
                    if (TimeZoneInfo.ConvertTimeFromUtc(ib.ExecutionStartUtc, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).Date < istTime.Date)
                    {
                        ib.IsHistorical = true;
                        currentDayStatus = "Live: Waiting for IB";
                    }
                }
                
                TimeSpan ibStartTime = new TimeSpan(9, 30, 0);
                TimeSpan ibEndTime = ibStartTime.Add(TimeSpan.FromMinutes(this.IBDurationMinutes));
                if (istTime.TimeOfDay >= ibEndTime && (!isIBCalculated || lastCalculatedDate != istTime.Date))
                {
                    if (ibEngine.CalculateIB(this.HistoricalData, this.Symbol, istTime, this.IBDurationMinutes, ProfileStepTicks, 40, out MarketData md, out bool isPrecise, this.LvnThreshold, this.Hvn2MinRatio))
                    {
                        CacheIB(md, istTime.Date, isPrecise, false);
                        isIBCalculated = true;
                        lastCalculatedDate = istTime.Date;
                    }
                }
            }

            if (this.CurrentChart == null) return;

            var graphics = args.Graphics;
            var mainWindow = this.CurrentChart.MainWindow;
            var converter = mainWindow.CoordinatesConverter;

            DateTime leftTime = converter.GetTime(mainWindow.ClientRectangle.Left);
            DateTime rightTime = converter.GetTime(mainWindow.ClientRectangle.Right);

            using (Pen redPen = new Pen(Color.Red, 2))
            using (Pen pocPen = new Pen(Color.Yellow, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (Pen hvn2Pen = new Pen(Color.Orange, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (Pen lvnPen = new Pen(Color.Magenta, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
            using (Pen dotPen = new Pen(Color.Gray, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
            using (Pen vertPen = new Pen(Color.DodgerBlue, 1))
            using (Font font = new Font("Arial", 8, FontStyle.Regular))
            using (Font debugFont = new Font("Arial", 12, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (SolidBrush debugBrush = new SolidBrush(Color.Yellow))
            using (SolidBrush ibBoxBrush = new SolidBrush(Color.FromArgb(30, Color.DodgerBlue)))
            using (Pen ibBoxPen = new Pen(Color.FromArgb(100, Color.DodgerBlue), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
            {
                StringFormat textFormat = new StringFormat { LineAlignment = StringAlignment.Far, Alignment = StringAlignment.Near };

                foreach (var ib in cachedIBs)
                {
                    if (ib.ExecutionEndUtc < leftTime || ib.ProfileStartUtc > rightTime)
                        continue;

                    int profStartX = (int)converter.GetChartX(ib.ProfileStartUtc);
                    int execStartX = (int)converter.GetChartX(ib.ExecutionStartUtc);
                    int execEndX = (int)converter.GetChartX(ib.ExecutionEndUtc);

                    execEndX = Math.Min(execEndX, mainWindow.ClientRectangle.Right);

                    int yVAH = (int)converter.GetChartY(ib.VAH);
                    int yVAL = (int)converter.GetChartY(ib.VAL);
                    int yPOC = (int)converter.GetChartY(ib.POC);
                    int yHigh = (int)converter.GetChartY(ib.High);
                    int yLow = (int)converter.GetChartY(ib.Low);

                    int rectWidth = execStartX - profStartX;
                    int rectHeight = yLow - yHigh;
                    if (rectWidth > 0 && rectHeight > 0)
                    {
                        graphics.FillRectangle(ibBoxBrush, profStartX, yHigh, rectWidth, rectHeight);
                        graphics.DrawRectangle(ibBoxPen, profStartX, yHigh, rectWidth, rectHeight);
                    }

                    graphics.DrawLine(redPen, profStartX, yVAH, execEndX, yVAH);
                    graphics.DrawLine(redPen, profStartX, yVAL, execEndX, yVAL);
                    graphics.DrawLine(pocPen, profStartX, yPOC, execEndX, yPOC);
                    graphics.DrawLine(dotPen, profStartX, yHigh, execEndX, yHigh);
                    graphics.DrawLine(dotPen, profStartX, yLow, execEndX, yLow);

                    if (ib.CurrentShape == VolumeProfileShape.BShape)
                    {
                        int yHVN2 = (int)converter.GetChartY(ib.HVN2);
                        int yLVN = (int)converter.GetChartY(ib.LVN);
                        graphics.DrawLine(hvn2Pen, profStartX, yHVN2, execEndX, yHVN2);
                        graphics.DrawLine(lvnPen, profStartX, yLVN, execEndX, yLVN);
                    }

                    if (LabelDisplay != LabelDisplayMode.None)
                    {
                        string GetText(string label, double price)
                        {
                            switch (LabelDisplay)
                            {
                                case LabelDisplayMode.Label: return label;
                                case LabelDisplayMode.Price: return price.ToString();
                                case LabelDisplayMode.Both: return $"{label} {price}";
                                default: return "";
                            }
                        }

                        int textX = Math.Min(execStartX, mainWindow.ClientRectangle.Right - 70);

                        // Left-side labels (VAH, VAL)
                        var leftLabels = new List<Tuple<string, int>>
                        {
                            Tuple.Create(GetText("VAH", ib.VAH), (int)yVAH),
                            Tuple.Create(GetText("VAL", ib.VAL), (int)yVAL)
                        }.OrderBy(x => x.Item2).ToList();

                        int lastY = -100;
                        foreach (var lbl in leftLabels)
                        {
                            int drawY = lbl.Item2;
                            if (drawY - lastY < 15) drawY = lastY + 15;
                            graphics.DrawString(lbl.Item1, font, textBrush, textX, drawY, textFormat);
                            lastY = drawY;
                        }

                        // Right-side labels (POC, High, Low, HVN2, LVN)
                        var rightLabels = new List<Tuple<string, int>>
                        {
                            Tuple.Create(GetText("POC", ib.POC), (int)yPOC),
                            Tuple.Create(GetText("High", ib.High), (int)yHigh),
                            Tuple.Create(GetText("Low", ib.Low), (int)yLow)
                        };

                        if (ib.CurrentShape == VolumeProfileShape.BShape)
                        {
                            rightLabels.Add(Tuple.Create(GetText("HVN2", ib.HVN2), (int)converter.GetChartY(ib.HVN2)));
                            rightLabels.Add(Tuple.Create(GetText("LVN", ib.LVN), (int)converter.GetChartY(ib.LVN)));
                        }

                        var sortedRight = rightLabels.OrderBy(x => x.Item2).ToList();
                        lastY = -100;
                        foreach (var lbl in sortedRight)
                        {
                            int drawY = lbl.Item2;
                            if (drawY - lastY < 15) drawY = lastY + 15;
                            graphics.DrawString(lbl.Item1, font, textBrush, execEndX, drawY, textFormat);
                            lastY = drawY;
                        }
                    }


                    // Draw Signal Info
                    if (ib.Signal != null && ib.CurrentShape != VolumeProfileShape.Unknown)
                    {
                        string shapeStr = ib.CurrentShape.ToString();
                        string signalText = $"Shape: {shapeStr} | Side: {ib.Signal.PreferredSide} | Entry: {ib.Signal.EntryPrice} | TP: {ib.Signal.TakeProfit} | SL: {ib.Signal.StopLoss}";

                        if (ib.IsHistorical && ShowHistoricalInfo)
                        {
                            using (SolidBrush tableBg = new SolidBrush(Color.FromArgb(180, 20, 20, 20)))
                            {
                                int tableWidth = 220;
                                int tableHeight = 150;
                                int tableX = profStartX;
                                int tableY = yLow + 10;
                                if (ib.CurrentShape == VolumeProfileShape.bShape || ib.CurrentShape == VolumeProfileShape.BShape)
                                    tableY = yHigh - tableHeight - 10;

                                graphics.FillRectangle(tableBg, tableX, tableY, tableWidth, tableHeight);
                                graphics.DrawRectangle(Pens.Gray, tableX, tableY, tableWidth, tableHeight);

                                graphics.DrawString("--- HISTORICAL IB SIGNAL ---", debugFont, textBrush, tableX + 10, tableY + 5);
                                
                                graphics.DrawString($"Shape: {shapeStr}", font, debugBrush, tableX + 10, tableY + 25);
                                graphics.DrawString($"Side:  {ib.Signal.PreferredSide}", font, textBrush, tableX + 10, tableY + 45);

                                string entryStr = $"Entry: {ib.Signal.EntryPrice}";
                                if (ib.Signal.EntryTime != null) entryStr += $" ({ib.Signal.EntryTime.Value:HH:mm})";
                                graphics.DrawString(entryStr, font, textBrush, tableX + 10, tableY + 65);

                                string tpStr = $"TP:    {ib.Signal.TakeProfit}";
                                if (ib.Signal.ExitReason == "TP Hit" && ib.Signal.ExitTime != null) tpStr += $" ({ib.Signal.ExitTime.Value:HH:mm})";
                                graphics.DrawString(tpStr, font, textBrush, tableX + 10, tableY + 85);

                                string slStr = $"SL:    {ib.Signal.StopLoss}";
                                if (ib.Signal.ExitReason == "SL Hit" && ib.Signal.ExitTime != null) slStr += $" ({ib.Signal.ExitTime.Value:HH:mm})";
                                graphics.DrawString(slStr, font, textBrush, tableX + 10, tableY + 105);

                                string statusColorStr = ib.Signal.Status;
                                if (ib.Signal.ExitReason != null) statusColorStr += $" ({ib.Signal.ExitReason})";
                                graphics.DrawString($"Status: {statusColorStr}", font, debugBrush, tableX + 10, tableY + 125);
                            }
                        }
                    }
                }
                
                if (ShowLiveInfoTable)
                {
                    using (SolidBrush tableBg = new SolidBrush(Color.FromArgb(180, 20, 20, 20)))
                    {
                        DailyIB liveIb = cachedIBs.FirstOrDefault(i => !i.IsHistorical);
                        bool hasSignal = liveIb != null && liveIb.Signal != null && liveIb.CurrentShape != VolumeProfileShape.Unknown;
                        
                        int tableWidth = 200;
                        int tableHeight = hasSignal ? 150 : 35;
                        if (ShowCacheInfo) tableHeight += 45;
                        
                        int tableX = mainWindow.ClientRectangle.Right - tableWidth - 10;
                        int tableY = 80;

                        graphics.FillRectangle(tableBg, tableX, tableY, tableWidth, tableHeight);
                        graphics.DrawRectangle(Pens.Gray, tableX, tableY, tableWidth, tableHeight);

                        graphics.DrawString("--- LIVE IB SIGNAL ---", debugFont, textBrush, tableX + 10, tableY + 5);

                        int cacheY = tableY + 30;

                        if (hasSignal)
                        {
                            graphics.DrawString($"Shape: {liveIb.CurrentShape}", font, debugBrush, tableX + 10, tableY + 25);
                            graphics.DrawString($"Side:  {liveIb.Signal.PreferredSide}", font, textBrush, tableX + 10, tableY + 45);

                            string entryStr = $"Entry: {liveIb.Signal.EntryPrice}";
                            if (liveIb.Signal.EntryTime != null) entryStr += $" ({liveIb.Signal.EntryTime.Value:HH:mm})";
                            graphics.DrawString(entryStr, font, textBrush, tableX + 10, tableY + 65);

                            string tpStr = $"TP:    {liveIb.Signal.TakeProfit}";
                            if (liveIb.Signal.ExitReason == "TP Hit" && liveIb.Signal.ExitTime != null) tpStr += $" ({liveIb.Signal.ExitTime.Value:HH:mm})";
                            graphics.DrawString(tpStr, font, textBrush, tableX + 10, tableY + 85);

                            string slStr = $"SL:    {liveIb.Signal.StopLoss}";
                            if (liveIb.Signal.ExitReason == "SL Hit" && liveIb.Signal.ExitTime != null) slStr += $" ({liveIb.Signal.ExitTime.Value:HH:mm})";
                            graphics.DrawString(slStr, font, textBrush, tableX + 10, tableY + 105);

                            string statusColorStr = liveIb.Signal.Status;
                            if (liveIb.Signal.ExitReason != null) statusColorStr += $" ({liveIb.Signal.ExitReason})";
                            graphics.DrawString($"Status: {statusColorStr}", font, debugBrush, tableX + 10, tableY + 125);
                            
                            cacheY = tableY + 150;
                        }

                        if (ShowCacheInfo)
                        {
                            graphics.DrawString(currentDayStatus, font, debugBrush, tableX + 10, cacheY);
                            graphics.DrawString(historyStatus, font, textBrush, tableX + 10, cacheY + 20);
                        }
                    }
                }
            }
        }

        private void GenerateIndicatorReport()
        {
            try
            {
                string csvFilePath = @"C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\IndicatorReport.csv";
                global::FVP_IB_Strategy.Calculations.ReportExporter.InitializeReport(csvFilePath);

                // Sort cached IBs by date ascending
                var sortedIBs = cachedIBs.OrderBy(x => x.ExecutionStartUtc).ToList();
                TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                foreach (var ib in sortedIBs)
                {
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
                            points = ib.Signal.PreferredSide == "BUY" ? (exitPrice - ib.Signal.EntryPrice) : (ib.Signal.EntryPrice - exitPrice);
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
                historyStatus += " [Report Generated]";
            }
            catch (Exception ex)
            {
                historyStatus += $" [Report Failed: {ex.Message}]";
            }
        }
    }
}

