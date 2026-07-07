using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;
using CustomStrategies.Calculations;

namespace CustomStrategies
{
    public partial class IBVisualizerIndicator
    {
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
                        
                        if (GenerateReport)
                        {
                            string csvFilePath = System.IO.Path.Combine(global::FVP_IB_Strategy.Config.ProjectPaths.GetBaseStrategyDirectory(), "IndicatorReport.csv");
                            AppendSingleIBToReport(ib, csvFilePath);
                        }
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

            System.Drawing.Region oldClip = graphics.Clip;
            graphics.SetClip(mainWindow.ClientRectangle);

            DateTime leftTime = converter.GetTime(mainWindow.ClientRectangle.Left);
            DateTime rightTime = converter.GetTime(mainWindow.ClientRectangle.Right);

            using (Font font = new Font("Arial", 8, FontStyle.Regular))
            using (Font debugFont = new Font("Arial", 12, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (SolidBrush debugBrush = new SolidBrush(Color.Yellow))
            {
                DrawHistoricalProfiles(graphics, mainWindow, leftTime, rightTime, font, debugFont, textBrush, debugBrush);
                
                int boxWidth = 330; // Increased to fit timestamps without clipping
                int baseX = mainWindow.ClientRectangle.Right - boxWidth - 10; // Anchored Top-Right with 10px padding
                int baseY = 20;

                DrawAIInsightsBox(graphics, font, baseX, ref baseY, boxWidth);
                DateTime currentIstTime = DateTime.MinValue;
                if (this.HistoricalData != null && this.HistoricalData.Count > 0)
                    currentIstTime = TimeZoneInfo.ConvertTimeFromUtc(((HistoryItemBar)this.HistoricalData[0]).TimeLeft, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));

                DrawLiveInfoTable(graphics, font, textBrush, debugBrush, baseX, baseY, boxWidth, currentIstTime);
            }

            graphics.Clip = oldClip;
        }

        private void DrawHistoricalProfiles(Graphics graphics, IChartWindow mainWindow, DateTime leftTime, DateTime rightTime, Font font, Font debugFont, SolidBrush textBrush, SolidBrush debugBrush)
        {
            var converter = mainWindow.CoordinatesConverter;
            using (Pen redPen = new Pen(Color.Red, 2))
            using (Pen pocPen = new Pen(Color.Yellow, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (Pen hvn2Pen = new Pen(Color.Orange, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (Pen lvnPen = new Pen(Color.Magenta, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
            using (Pen dotPen = new Pen(Color.Gray, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
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
            }
        }

        private void DrawAIInsightsBox(Graphics graphics, Font font, int baseX, ref int baseY, int boxWidth)
        {
            if (ShowAIInsightsBox)
            {
                using (SolidBrush tableBg = new SolidBrush(Color.FromArgb(200, 25, 25, 25)))
                using (Pen tableBorder = new Pen(Color.FromArgb(100, 100, 100), 1))
                {
                    int aiHeight = 25;
                    graphics.FillRectangle(tableBg, baseX, baseY, boxWidth, aiHeight);
                    graphics.DrawRectangle(tableBorder, baseX, baseY, boxWidth, aiHeight);

                    string aiText = this.EnableCogneeWebhook ? quantInsight : "[AI] Insight: AI Disabled";
                    Color aiColor = this.EnableCogneeWebhook ? Color.Gold : Color.Gray;
                    graphics.DrawString(aiText, font, new SolidBrush(aiColor), baseX + 5, baseY + 5);

                    baseY += aiHeight + 10; // Spacing between boxes
                }
            }
        }

        private void DrawLiveInfoTable(Graphics graphics, Font font, SolidBrush textBrush, SolidBrush debugBrush, int baseX, int baseY, int boxWidth, DateTime currentChartTime)
        {
            if (ShowLiveInfoTable)
            {
                using (SolidBrush tableBg = new SolidBrush(Color.FromArgb(200, 25, 25, 25)))
                using (Pen tableBorder = new Pen(Color.FromArgb(100, 100, 100), 1))
                using (Pen dividerPen = new Pen(Color.FromArgb(80, 80, 80), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                {
                    DailyIB liveIb = cachedIBs.FirstOrDefault(i => !i.IsHistorical);
                    bool hasSignal = liveIb != null && liveIb.Signal != null && liveIb.CurrentShape != VolumeProfileShape.Unknown;

                    // --- 2. LIVE IB SIGNAL BOX ---
                    int ibHeight = hasSignal ? 90 : 35;
                    if (ShowCacheInfo) ibHeight += 35;
                    graphics.FillRectangle(tableBg, baseX, baseY, boxWidth, ibHeight);
                    graphics.DrawRectangle(tableBorder, baseX, baseY, boxWidth, ibHeight);

                    // Header Row
                    string headerText = "LIVE IB SIGNAL";
                    if (hasSignal) 
                    {
                        // Signal is calculated at ExecutionStartUtc
                        DateTime calcTime = TimeZoneInfo.ConvertTimeFromUtc(liveIb.ExecutionStartUtc, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
                        headerText += $" ({calcTime:dd-MM-yyyy - HH:mm})";
                    }
                    graphics.DrawString(headerText, font, debugBrush, baseX + 5, baseY + 5);
                    graphics.DrawLine(tableBorder, baseX, baseY + 22, baseX + boxWidth, baseY + 22);

                    if (hasSignal)
                    {
                        // Calculate Dynamic Bias
                        string biasStr = "NONE";
                        Color biasColor = Color.White;
                        if (liveIb.Signal.TakeProfit > liveIb.Signal.EntryPrice)
                        {
                            biasStr = "LONG";
                            biasColor = Color.LimeGreen;
                        }
                        else if (liveIb.Signal.TakeProfit < liveIb.Signal.EntryPrice)
                        {
                            biasStr = "SHORT";
                            biasColor = Color.Tomato;
                        }

                        // Calculate Status & Colors safely (Visual Only)
                        string displayStatus = liveIb.Signal.Status;
                        Color statusColor = Color.Gray; // Default Waiting
                        
                        if (displayStatus == "Waiting")
                        {
                            double currentPrice = this.Symbol.Last;
                            double distanceToEntry = Math.Abs(currentPrice - liveIb.Signal.EntryPrice);
                            double threshold = this.Symbol.TickSize * 10; // 10 ticks

                            if (distanceToEntry <= threshold)
                            {
                                statusColor = Color.Yellow;
                                displayStatus = "Approaching";
                            }
                        }
                        else if (displayStatus == "In Trade")
                        {
                            statusColor = (biasStr == "LONG") ? Color.LimeGreen : Color.Tomato;
                            displayStatus = "Active";
                        }
                        else if (displayStatus == "Closed")
                        {
                            statusColor = (liveIb.Signal.ExitReason == "TP Hit") ? Color.LimeGreen : Color.Tomato;
                            if (!string.IsNullOrEmpty(liveIb.Signal.ExitReason)) displayStatus = liveIb.Signal.ExitReason;
                        }

                        int row1Y = baseY + 27;
                        int row2Y = baseY + 45;
                        int row3Y = baseY + 68;

                        int col1X = baseX + 5;
                        int col2X = baseX + 130;

                        // Left Column (Shape / Side)
                        graphics.DrawString($"Shape: {liveIb.CurrentShape}", font, textBrush, col1X, row1Y);
                        graphics.DrawString($"Side:  {liveIb.Signal.PreferredSide}", font, textBrush, col1X, row2Y);

                        // Vertical Divider
                        graphics.DrawLine(dividerPen, col2X - 5, baseY + 22, col2X - 5, baseY + 63);

                        // Right Column (Bias / Status)
                        graphics.DrawString("Bias: ", font, textBrush, col2X, row1Y);
                        graphics.DrawString(biasStr, font, new SolidBrush(biasColor), col2X + 35, row1Y);
                        
                        graphics.DrawString("Stat: ", font, textBrush, col2X, row2Y);
                        graphics.DrawString(displayStatus, font, new SolidBrush(statusColor), col2X + 30, row2Y);

                        // Horizontal Divider
                        graphics.DrawLine(tableBorder, baseX, baseY + 63, baseX + boxWidth, baseY + 63);

                        // Bottom Row (Entry / TP / SL)
                        int botCol1X = baseX + 5;
                        int botCol2X = baseX + 105;
                        int botCol3X = baseX + 205;
                        
                        string entryStr = $"Entry: {liveIb.Signal.EntryPrice}";
                        if (liveIb.Signal.EntryTime.HasValue) entryStr += $" ({liveIb.Signal.EntryTime.Value:HH:mm})";
                        
                        string tpStr = $"TP: {liveIb.Signal.TakeProfit}";
                        if (liveIb.Signal.ExitReason == "TP Hit" && liveIb.Signal.ExitTime.HasValue) tpStr += $" ({liveIb.Signal.ExitTime.Value:HH:mm})";
                        
                        string slStr = $"SL: {liveIb.Signal.StopLoss}";
                        if (liveIb.Signal.ExitReason == "SL Hit" && liveIb.Signal.ExitTime.HasValue) slStr += $" ({liveIb.Signal.ExitTime.Value:HH:mm})";

                        graphics.DrawString(entryStr, font, textBrush, botCol1X, row3Y);
                        graphics.DrawString(tpStr, font, textBrush, botCol2X, row3Y);
                        graphics.DrawString(slStr, font, textBrush, botCol3X, row3Y);
                    }

                    if (ShowCacheInfo)
                    {
                        int cacheY = hasSignal ? baseY + 90 : baseY + 35;
                        graphics.DrawLine(tableBorder, baseX, cacheY, baseX + boxWidth, cacheY);
                        graphics.DrawString(currentDayStatus, font, debugBrush, baseX + 5, cacheY + 5);
                        graphics.DrawString(historyStatus, font, textBrush, baseX + 5, cacheY + 20);
                    }
                }
            }
        }
    }
}
