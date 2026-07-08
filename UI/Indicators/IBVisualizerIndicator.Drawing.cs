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
                // Calculation handled strictly by OnUpdate sequentially
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
                
                int boxWidth = 320; // Widen base width so baseX shifts left properly
                int baseX = mainWindow.ClientRectangle.Right - boxWidth - 10; // Anchored Top-Right with 10px padding
                int baseY = 20;

                DailyIB liveIb = cachedIBs.FirstOrDefault(i => !i.IsHistorical);
                DrawAIInsightsBox(graphics, font, baseX, ref baseY, boxWidth, liveIb);
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
                StringFormat leftFormat = new StringFormat { LineAlignment = StringAlignment.Far, Alignment = StringAlignment.Near };
                StringFormat rightFormat = new StringFormat { LineAlignment = StringAlignment.Far, Alignment = StringAlignment.Far };

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
                            graphics.DrawString(lbl.Item1, font, textBrush, textX, drawY, leftFormat);
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
                            graphics.DrawString(lbl.Item1, font, textBrush, execEndX, drawY, rightFormat);
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

        private void DrawAIInsightsBox(Graphics graphics, Font font, int baseX, ref int baseY, int boxWidth, DailyIB liveIb)
        {
            if (ShowAIInsightsBox)
            {
                using (SolidBrush tableBg = new SolidBrush(Color.FromArgb(200, 25, 25, 25)))
                using (Pen tableBorder = new Pen(Color.FromArgb(100, 100, 100), 1))
                using (Pen dividerPen = new Pen(Color.FromArgb(80, 80, 80), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                using (SolidBrush grayBrush = new SolidBrush(Color.Silver))
                {
                    int actualBoxWidth = Math.Max(boxWidth, 320);
                    int aiHeight = 65;
                    graphics.FillRectangle(tableBg, baseX, baseY, actualBoxWidth, aiHeight);
                    graphics.DrawRectangle(tableBorder, baseX, baseY, actualBoxWidth, aiHeight);

                    System.Drawing.Region oldClip = graphics.Clip;
                    graphics.SetClip(new Rectangle(baseX, baseY, actualBoxWidth, aiHeight));

                    string aiText = this.EnableCogneeWebhook ? "AI ENGINE STATE [ONLINE]" : "AI ENGINE STATE [OFFLINE]";
                    Color aiColor = this.EnableCogneeWebhook ? Color.Gold : Color.Gray;
                    graphics.DrawString(aiText, font, new SolidBrush(aiColor), baseX + 5, baseY + 5);
                    
                    graphics.DrawString($"Ping: {serverLivenessStatus.Replace("Server: ", "")}", font, new SolidBrush(serverLivenessColor), baseX + actualBoxWidth - 75, baseY + 5);
                    
                    graphics.DrawLine(dividerPen, baseX, baseY + 25, baseX + actualBoxWidth, baseY + 25);
                    
                    string probText = "Offline";
                    if (this.EnableCogneeWebhook)
                    {
                        probText = quantInsight.Replace("Quant Insight: ", "").Replace("Probability ", "").Replace("Score ", "").Replace("Confidence ", "");
                        if (!probText.Contains("%") && probText != "AI Disabled" && probText != "JSON Parse Error" && probText != "Waiting for 10:00 AM...") 
                            probText += "%";
                    }
                    
                    int col1X = baseX + 5;
                    int col2X = baseX + (actualBoxWidth / 2) + 5;

                    graphics.DrawString("Confidence Score: ", font, grayBrush, col1X, baseY + 28);
                    graphics.DrawString(probText, font, new SolidBrush(Color.White), col1X + 98, baseY + 28);

                    graphics.DrawString("Win Rate: ", font, grayBrush, col2X, baseY + 28);
                    graphics.DrawString("N/A", font, new SolidBrush(Color.White), col2X + 60, baseY + 28);

                    graphics.DrawLine(dividerPen, baseX + (actualBoxWidth / 2), baseY + 25, baseX + (actualBoxWidth / 2), baseY + aiHeight);

                    string p1Status = hasSentToCogneeToday ? "Sent" : "Pending";
                    Color p1Color = hasSentToCogneeToday ? Color.LimeGreen : Color.Gray;
                    
                    bool p2Sent = liveIb != null && liveIb.Signal != null && liveIb.Signal.IsMemoryPayloadSent;
                    string p2Status = p2Sent ? "Sent" : "Pending";
                    Color p2Color = p2Sent ? Color.LimeGreen : Color.Gray;
                    
                    graphics.DrawString("Payload 1: ", font, grayBrush, col1X, baseY + 45);
                    graphics.DrawString(p1Status, font, new SolidBrush(p1Color), col1X + 65, baseY + 45);
                    
                    graphics.DrawString("Payload 2: ", font, grayBrush, col2X, baseY + 45);
                    graphics.DrawString(p2Status, font, new SolidBrush(p2Color), col2X + 65, baseY + 45);

                    graphics.Clip = oldClip;

                    baseY += aiHeight + 10;
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
                using (SolidBrush grayBrush = new SolidBrush(Color.Silver))
                {
                    DailyIB liveIb = cachedIBs.FirstOrDefault(i => !i.IsHistorical);
                    bool hasSignal = liveIb != null && liveIb.Signal != null && liveIb.CurrentShape != VolumeProfileShape.Unknown;

                    int actualBoxWidth = Math.Max(boxWidth, 320);
                    int ibHeight = hasSignal ? 110 : 35;
                    if (ShowCacheInfo) ibHeight += 35;
                    graphics.FillRectangle(tableBg, baseX, baseY, actualBoxWidth, ibHeight);
                    graphics.DrawRectangle(tableBorder, baseX, baseY, actualBoxWidth, ibHeight);

                    System.Drawing.Region oldClip = graphics.Clip;
                    graphics.SetClip(new Rectangle(baseX, baseY, actualBoxWidth, ibHeight));

                    string headerText = "EXECUTION MATRIX";
                    if (hasSignal) 
                    {
                        DateTime calcTime = TimeZoneInfo.ConvertTimeFromUtc(liveIb.ExecutionStartUtc, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
                        headerText += $"   ({calcTime:HH:mm:ss})";
                    }
                    graphics.DrawString(headerText, font, new SolidBrush(Color.Gold), baseX + 5, baseY + 5);
                    graphics.DrawLine(tableBorder, baseX, baseY + 22, baseX + actualBoxWidth, baseY + 22);

                    if (hasSignal)
                    {
                        string explicitBias = liveIb.Signal.TakeProfit > liveIb.Signal.EntryPrice ? "LONG" : "SHORT";
                        string displayStatus = liveIb.Signal.Status;
                        Color statusColor = Color.Gray; 
                        
                        if (displayStatus == "Waiting")
                        {
                            double currentPrice = this.Symbol.Last;
                            double distanceToEntry = Math.Abs(currentPrice - liveIb.Signal.EntryPrice);
                            double threshold = this.Symbol.TickSize * 10;

                            if (distanceToEntry <= threshold)
                            {
                                statusColor = Color.Yellow;
                                displayStatus = "Approaching";
                            }
                        }
                        else if (displayStatus == "In Trade")
                        {
                            statusColor = (explicitBias == "LONG") ? Color.LimeGreen : Color.Tomato;
                            displayStatus = "ACTIVE";
                        }
                        else if (displayStatus == "Closed")
                        {
                            statusColor = (liveIb.Signal.ExitReason == "TP Hit") ? Color.LimeGreen : Color.Tomato;
                            if (!string.IsNullOrEmpty(liveIb.Signal.ExitReason)) displayStatus = liveIb.Signal.ExitReason;
                        }

                        int row1Y = baseY + 25;
                        int row2Y = baseY + 40;
                        int row3Y = baseY + 55;
                        int dividerY = baseY + 71;
                        int row4Y = baseY + 77;
                        int row5Y = baseY + 92;

                        int col1X = baseX + 5;
                        int col2X = baseX + (actualBoxWidth / 2) + 5;

                        string pocStr = liveIb.POC.ToString();
                        string vahStr = liveIb.VAH.ToString();
                        string valStr = liveIb.VAL.ToString();
                        string timeSuffix = "";

                        if (liveIb.LastUpdatedUtc != DateTime.MinValue)
                        {
                            TimeZoneInfo istTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                            DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(liveIb.LastUpdatedUtc, istTz);
                            timeSuffix = $"({localTime:HH:mm})";
                        }

                        // Tier 1: Shape / POC
                        graphics.DrawString("Shape: ", font, grayBrush, col1X, row1Y);
                        graphics.DrawString(liveIb.CurrentShape.ToString(), font, textBrush, col1X + 45, row1Y);
                        
                        graphics.DrawString("POC: ", font, grayBrush, col2X, row1Y);
                        graphics.DrawString(pocStr, font, textBrush, col2X + 35, row1Y);
                        if (!string.IsNullOrEmpty(timeSuffix)) graphics.DrawString(timeSuffix, font, grayBrush, col2X + 90, row1Y);

                        // Tier 2: Side / VAH
                        graphics.DrawString("Side: ", font, grayBrush, col1X, row2Y);
                        
                        string sideStr = liveIb.Signal.PreferredSide;
                        if (sideStr == "FADE" || sideStr == "BREAKOUT") sideStr += $" ({explicitBias})";
                        Color sideColor = (sideStr.Contains("LONG") || sideStr.Contains("BUY")) ? Color.LimeGreen : 
                                          (sideStr.Contains("SHORT") || sideStr.Contains("SELL")) ? Color.Tomato : Color.White;
                        graphics.DrawString(sideStr, font, new SolidBrush(sideColor), col1X + 35, row2Y);

                        graphics.DrawString("VAH: ", font, grayBrush, col2X, row2Y);
                        graphics.DrawString(vahStr, font, textBrush, col2X + 35, row2Y);
                        if (!string.IsNullOrEmpty(timeSuffix)) graphics.DrawString(timeSuffix, font, grayBrush, col2X + 90, row2Y);

                        // Tier 3: VAL / HVN/LVN
                        graphics.DrawString("VAL: ", font, grayBrush, col1X, row3Y);
                        graphics.DrawString(valStr, font, textBrush, col1X + 35, row3Y);
                        if (!string.IsNullOrEmpty(timeSuffix)) graphics.DrawString(timeSuffix, font, grayBrush, col1X + 90, row3Y);

                        if (liveIb.CurrentShape == VolumeProfileShape.BShape)
                        {
                            string hvnLvnStr = $"{liveIb.HVN2}/{liveIb.LVN}";
                            graphics.DrawString("HVN/LVN: ", font, grayBrush, col2X, row3Y);
                            graphics.DrawString(hvnLvnStr, font, textBrush, col2X + 60, row3Y);
                        }

                        // Horizontal Divider
                        graphics.DrawLine(tableBorder, baseX, dividerY, baseX + actualBoxWidth, dividerY);

                        // Vertical Dividers
                        graphics.DrawLine(dividerPen, baseX + (actualBoxWidth / 2), baseY + 25, baseX + (actualBoxWidth / 2), dividerY);
                        graphics.DrawLine(dividerPen, baseX + (actualBoxWidth / 2), dividerY, baseX + (actualBoxWidth / 2), baseY + ibHeight);

                        // Tier 4: ENT / Status
                        string entryStr = $"{liveIb.Signal.EntryPrice}";
                        if (liveIb.Signal.EntryTime.HasValue)
                            entryStr += $"  ({liveIb.Signal.EntryTime.Value:HH:mm})";

                        graphics.DrawString("ENT: ", font, grayBrush, col1X, row4Y);
                        graphics.DrawString(entryStr, font, new SolidBrush(Color.Gold), col1X + 35, row4Y);

                        graphics.DrawString("Status: ", font, grayBrush, col2X, row4Y);
                        graphics.DrawString(displayStatus, font, new SolidBrush(statusColor), col2X + 45, row4Y);

                        // Tier 5: TP / SL
                        string tpStr = $"{liveIb.Signal.TakeProfit}";
                        if (liveIb.Signal.ExitReason == "TP Hit" && liveIb.Signal.ExitTime.HasValue)
                            tpStr += $" ({liveIb.Signal.ExitTime.Value:HH:mm})";

                        string slStr = $"{liveIb.Signal.StopLoss}";
                        if (liveIb.Signal.ExitReason == "SL Hit" && liveIb.Signal.ExitTime.HasValue)
                            slStr += $" ({liveIb.Signal.ExitTime.Value:HH:mm})";

                        graphics.DrawString("TP: ", font, grayBrush, col1X, row5Y);
                        graphics.DrawString(tpStr, font, new SolidBrush(Color.LimeGreen), col1X + 30, row5Y);

                        graphics.DrawString("SL: ", font, grayBrush, col2X, row5Y);
                        graphics.DrawString(slStr, font, new SolidBrush(Color.Tomato), col2X + 30, row5Y);
                    }

                    if (ShowCacheInfo)
                    {
                        int cacheY = hasSignal ? baseY + 110 : baseY + 35;
                        graphics.DrawLine(tableBorder, baseX, cacheY, baseX + actualBoxWidth, cacheY);
                        
                        string cacheTimeStr = currentChartTime > DateTime.MinValue ? $" ({currentChartTime:yyyy-MM-dd HH:mm:ss})" : "";
                        
                        string mainStatus = currentDayStatus.Replace("Live: ", "");
                        Color statusColor = Color.White;
                        if (mainStatus.Contains("Fallback")) statusColor = Color.Tomato;
                        else if (mainStatus.Contains("Precise")) statusColor = Color.LimeGreen;
                        else if (mainStatus.Contains("Initializing")) statusColor = Color.Gold;

                        if (currentDayStatus.StartsWith("Live: "))
                        {
                            graphics.DrawString("Live: ", font, grayBrush, baseX + 5, cacheY + 5);
                            graphics.DrawString(mainStatus + cacheTimeStr, font, new SolidBrush(statusColor), baseX + 40, cacheY + 5);
                        }
                        else
                        {
                            graphics.DrawString(currentDayStatus + cacheTimeStr, font, new SolidBrush(statusColor), baseX + 5, cacheY + 5);
                        }

                        graphics.DrawString(historyStatus, font, textBrush, baseX + 5, cacheY + 20);
                    }

                    graphics.Clip = oldClip;
                }
            }
        }
    }
}
