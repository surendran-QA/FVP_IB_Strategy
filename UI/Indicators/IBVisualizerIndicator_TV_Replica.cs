using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;

namespace CustomStrategies
{
    // This is a replica of the TradingView logic where binning starts at 0.00
    // and uses 1-minute basic volume division instead of backend VolumeAnalysis.
    // Saved for future reference. DO NOT COMPILE by default.
    public class IBVisualizerIndicator_TV_Replica : Indicator
    {
        [InputParameter("Start Trading Time (EST)", 0)]
        public TimeSpan StartTradingTime { get; set; } = new TimeSpan(10, 0, 0);

        [InputParameter("End Trading Time (EST)", 1)]
        public TimeSpan EndTradingTime { get; set; } = new TimeSpan(16, 0, 0);

        [InputParameter("Profile Step (Ticks)", 2, minimum: 1, maximum: 100)]
        public int ProfileStepTicks { get; set; } = 4;

        [InputParameter("Show Labels", 3)]
        public bool ShowLabels { get; set; } = true;

        [InputParameter("Show Live Info Table", 4)]
        public bool ShowLiveInfoTable { get; set; } = true;

        [InputParameter("Show Historical Info Text", 11)]
        public bool ShowHistoricalInfo { get; set; } = true;

        [InputParameter("Show Cache Info", 12)]
        public bool ShowCacheInfo { get; set; } = true;

        private bool isIBCalculated = false;
        private DateTime lastCalculatedDate = DateTime.MinValue;
        
        private List<DailyIB> cachedIBs = new List<DailyIB>();
        private TvReplicaBalanceEngine engine = new TvReplicaBalanceEngine();

        public IBVisualizerIndicator_TV_Replica() 
        {
            this.Name = "IB Visualizer (TV Replica) v1.1";
            this.SeparateWindow = false;
        }

        protected override void OnInit()
        {
            isIBCalculated = false;
            lastCalculatedDate = DateTime.MinValue;
            cachedIBs.Clear();
            base.OnInit();
        }

        protected override void OnUpdate(UpdateArgs args)
        {
            if (this.HistoricalData.Count < 2) return;

            var currentBar = (HistoryItemBar)this.HistoricalData[0];
            DateTime istTime = TimeZoneInfo.ConvertTimeFromUtc(currentBar.TimeLeft, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            TimeSpan currentTime = istTime.TimeOfDay;

            bool isIBPhase = currentTime >= new TimeSpan(9, 30, 0) && currentTime < new TimeSpan(10, 0, 0);

            if (isIBPhase)
            {
                isIBCalculated = false;
                return;
            }

            if (currentTime >= new TimeSpan(10, 0, 0))
            {
                if (engine.CalculateIB(this.Symbol, istTime, new TimeSpan(9, 30, 0), StartTradingTime, ProfileStepTicks, out MarketData md))
                {
                    ExecutionSimulator sim = new ExecutionSimulator();
                    sim.SimulateExecution(md, this.HistoricalData, new TimeSpan(10, 0, 0), EndTradingTime, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));

                    CacheIB(md, istTime.Date);
                    isIBCalculated = true;
                    lastCalculatedDate = istTime.Date;
                }
            }
        }

        private void CacheIB(MarketData md, DateTime currentSimDate)
        {
            TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime profileStartIst = currentSimDate.Add(new TimeSpan(9, 30, 0));
            DateTime execStartIst = currentSimDate.Add(StartTradingTime);

            DateTime sessionEndIst = EndTradingTime < StartTradingTime 
                ? currentSimDate.AddDays(1).Add(EndTradingTime) 
                : currentSimDate.Add(EndTradingTime);

            cachedIBs.Add(new DailyIB {
                ProfileStartUtc = TimeZoneInfo.ConvertTimeToUtc(profileStartIst, istTz),
                ExecutionStartUtc = TimeZoneInfo.ConvertTimeToUtc(execStartIst, istTz),
                ExecutionEndUtc = TimeZoneInfo.ConvertTimeToUtc(sessionEndIst, istTz),
                VAH = md.IB_VAH,
                VAL = md.IB_VAL,
                POC = md.IB_POC,
                High = md.IB_High,
                Low = md.IB_Low,
                IsPrecise = false,
                CurrentShape = md.CurrentShape,
                Signal = md.Signal,
                IsHistorical = false // Simplified for TV replica since it doesn't loop history yet
            });
        }
        public override void OnPaintChart(PaintChartEventArgs args)
        {
            if (this.CurrentChart == null) return;

            var graphics = args.Graphics;
            var mainWindow = this.CurrentChart.MainWindow;
            var converter = mainWindow.CoordinatesConverter;

            DateTime leftTime = converter.GetTime(mainWindow.ClientRectangle.Left);
            DateTime rightTime = converter.GetTime(mainWindow.ClientRectangle.Right);

            using (Pen redPen = new Pen(Color.Red, 2))
            using (Pen pocPen = new Pen(Color.Yellow, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
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

                    if (ShowLabels)
                    {
                        int textX = Math.Min(execStartX, mainWindow.ClientRectangle.Right - 70);

                        graphics.DrawString(ib.VAH.ToString(), font, textBrush, textX, yVAH, textFormat);
                        graphics.DrawString(ib.VAL.ToString(), font, textBrush, textX, yVAL, textFormat);
                        graphics.DrawString(ib.POC.ToString(), font, textBrush, textX, yPOC, textFormat);
                        graphics.DrawString(ib.High.ToString(), font, textBrush, textX, yHigh, textFormat);
                        graphics.DrawString(ib.Low.ToString(), font, textBrush, textX, yLow, textFormat);
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
                        
                        int tableWidth = 320;
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
                            graphics.DrawString("Live: Initializing (TV Replica)", font, debugBrush, tableX + 10, cacheY);
                        }
                    }
                }
            }
        }
    }
}

