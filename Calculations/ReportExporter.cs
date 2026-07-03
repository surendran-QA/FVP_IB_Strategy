using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FVP_IB_Strategy.Calculations
{
    public static class ReportExporter
    {
        private static readonly object _lockObj = new object();
        private static Dictionary<string, List<string>> _reportBuffers = new Dictionary<string, List<string>>();
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly SemaphoreSlim _memoryThrottle = new SemaphoreSlim(1, 1);

        public static void InitializeReport(string filePath)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    _reportBuffers[filePath] = new List<string> { "Day,OrderPlacedTime,EntryFillTime,ExitTime,Symbol,Side,Qty,EntryPrice,ExitPrice,PnL,Status,Result,Shape,IB_High,IB_Low,POC,VAH,VAL,LVN,HVN1,HVN2" };
                    
                    // --- IMMEDIATE HEADER FLUSH TO DISK (Immune to OnStop not firing in Background mode!) ---
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize and flush buffer for {filePath}: {ex.Message}");
                }
            }
        }

        public static void AppendReportRow(
            string filePath, 
            string day, 
            string orderPlacedTime, 
            string entryFillTime, 
            string exitTime, 
            string symbol, 
            string side, 
            double qty, 
            string entryPrice, 
            string exitPrice, 
            string pnl, 
            string status, 
            string result, 
            string shape, 
            string ibHigh, 
            string ibLow, 
            string poc, 
            string vah, 
            string val, 
            string lvn, 
            string hvn1, 
            string hvn2)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    string row = $"{day},{orderPlacedTime},{entryFillTime},{exitTime},{symbol},{side},{qty},{entryPrice},{exitPrice},{pnl},{status},{result},{shape},{ibHigh},{ibLow},{poc},{vah},{val},{lvn},{hvn1},{hvn2}";
                    
                    if (!_reportBuffers.ContainsKey(filePath))
                    {
                        _reportBuffers[filePath] = new List<string> { "Day,OrderPlacedTime,EntryFillTime,ExitTime,Symbol,Side,Qty,EntryPrice,ExitPrice,PnL,Status,Result,Shape,IB_High,IB_Low,POC,VAH,VAL,LVN,HVN1,HVN2" };
                    }
                    _reportBuffers[filePath].Add(row);

                    // --- IMMEDIATE ATOMIC FLUSH TO DISK (Immune to OnStop not firing in Background mode!) ---
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to append and flush row for {filePath}: {ex.Message}");
                }
            }
        }

        public static void FlushReports(string tradeReportPath, string allSignalsPath)
        {
            lock (_lockObj)
            {
                try
                {
                    FlushSingleReport(tradeReportPath);
                    FlushSingleReport(allSignalsPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to flush in-memory reports to disk: {ex.Message}");
                }
            }
        }

        // ---- DIAGNOSTIC REPORT ----

        public static void InitializeDiagReport(string filePath)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    _reportBuffers[filePath] = new List<string>
                    {
                        "Date,DayOfWeek,BufferBars,IsPrecise,IB_High,IB_Low,POC,VAH,VAL,Shape,Signal,EntryPrice,StopLoss,TakeProfit"
                    };
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize diag report {filePath}: {ex.Message}");
                }
            }
        }

        public static void AppendDiagRow(
            string filePath, string date, string dayOfWeek, int bufferBars, bool isPrecise,
            double ibHigh, double ibLow, double poc, double vah, double val,
            string shape, string signal, double entryPrice, double stopLoss, double takeProfit)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    string ep = double.IsNaN(entryPrice) ? "-" : entryPrice.ToString("F2");
                    string sl = double.IsNaN(stopLoss)   ? "-" : stopLoss.ToString("F2");
                    string tp = double.IsNaN(takeProfit) ? "-" : takeProfit.ToString("F2");
                    string row = $"{date},{dayOfWeek},{bufferBars},{isPrecise},{ibHigh:F2},{ibLow:F2},{poc:F2},{vah:F2},{val:F2},{shape},{signal},{ep},{sl},{tp}";

                    if (!_reportBuffers.ContainsKey(filePath))
                        _reportBuffers[filePath] = new List<string> { "Date,DayOfWeek,BufferBars,IsPrecise,IB_High,IB_Low,POC,VAH,VAL,Shape,Signal,EntryPrice,StopLoss,TakeProfit" };

                    _reportBuffers[filePath].Add(row);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to append diag row to {filePath}: {ex.Message}");
                }
            }
        }

        private static void FlushSingleReport(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            if (_reportBuffers.TryGetValue(filePath, out var rows) && rows.Count > 0)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, rows);
                    _reportBuffers.Remove(filePath); // Clear from memory after successful flush!
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write file {filePath}: {ex.Message}");
                }
            }
        }

        public static async Task<string> AnalyzeSetupAsync(
            string assetName, 
            DateTime barTime,
            string ibShape, 
            string ibHvn1,
            string ibHvn2,
            string ibLvn,
            double ibHigh,
            double ibLow,
            double ibPoc,
            double ibVah,
            double ibVal,
            bool enableWebhook)
        {
            string date = barTime.ToString("yyyy-MM-dd");
            string time = barTime.ToString("HH:mm:ss");
            string sessionId = $"{assetName}_{date}";

            string payload = $@"
[SESSION ID: {sessionId}]
[MARKET CONTEXT NODE]
Asset: {assetName}
Timestamp: {date} {time}

[STRUCTURAL STATE NODE]
Profile Shape: {ibShape}
Session Extremes: IB_High {ibHigh} | IB_Low {ibLow}
Value Area: VAH {ibVah} | POC {ibPoc} | VAL {ibVal}
Microstructure: HVN1 {ibHvn1} | HVN2 {ibHvn2} | LVN_Gap {ibLvn}
";
            
            // Global Logging
            lock (_lockObj)
            {
                try
                {
                    string logPath = @"C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\AI_Global_Events.log";
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [HIT 1: MORNING SETUP] {sessionId}" + Environment.NewLine + payload + Environment.NewLine);
                }
                catch { }
            }

            if (!enableWebhook) return "Webhook Disabled";

            await _memoryThrottle.WaitAsync();
            try
            {
                string safePayload = payload.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
                string jsonPayload = $"{{\"payload\": \"{safePayload}\"}}";
                
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("http://127.0.0.1:8000/analyze", content);
                string responseStr = await response.Content.ReadAsStringAsync();
                
                // Parse AI Score manually to avoid dependency issues
                string aiScore = "N/A";
                string winProb = "N/A";
                
                var scoreMatch = System.Text.RegularExpressions.Regex.Match(responseStr, "\"ai_score\":\\s*\"([^\"]+)\"");
                if (scoreMatch.Success) aiScore = scoreMatch.Groups[1].Value;
                
                var probMatch = System.Text.RegularExpressions.Regex.Match(responseStr, "\"win_probability\":\\s*\"([^\"]+)\"");
                if (probMatch.Success) winProb = probMatch.Groups[1].Value;

                return $"AI Advice: Score {aiScore} | Probability {winProb}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to analyze setup: {ex.Message}");
                return $"AI Advice: Error - {ex.Message}";
            }
            finally
            {
                _memoryThrottle.Release();
            }
        }

        public static void AppendCogneePayload(
            string filePath, 
            string assetName, 
            DateTime barTime,
            string ibShape, 
            string tradingSignal, 
            string tradeResult, 
            string exitReason,
            string ibHvn1,
            string ibHvn2,
            string ibLvn,
            double ibHigh,
            double ibLow,
            double ibPoc,
            double ibVah,
            double ibVal,
            double entryPrice,
            double stopLoss,
            double takeProfit,
            bool enableWebhook,
            bool autoCognify)
        {
            Task.Run(async () => {
                string date = barTime.ToString("yyyy-MM-dd");
                string time = barTime.ToString("HH:mm:ss");
                string sessionId = $"{assetName}_{date}";

                string payload = $@"
[SESSION ID: {sessionId}]
[MARKET CONTEXT NODE]
Asset: {assetName}
Timestamp: {date} {time}

[STRUCTURAL STATE NODE]
Profile Shape: {ibShape}
Session Extremes: IB_High {ibHigh} | IB_Low {ibLow}
Value Area: VAH {ibVah} | POC {ibPoc} | VAL {ibVal}
Microstructure: HVN1 {ibHvn1} | HVN2 {ibHvn2} | LVN_Gap {ibLvn}

[EXECUTION PLAN NODE]
System Bias: {tradingSignal}
Order Setup: Entry {entryPrice} | Take Profit {takeProfit} | Stop Loss {stopLoss}

[RELATIONAL SUMMARY]
At {time} on {date}, {assetName} established its Initial Balance, resolving into a {ibShape} distribution. 
Primary institutional value is anchored at the POC of {ibPoc}, contained within the VAH ({ibVah}) and VAL ({ibVal}) boundaries. Session liquidity extremes are marked at a High of {ibHigh} and a Low of {ibLow}. 
Microstructure analysis dictates primary liquidity sitting at HVN1 ({ibHvn1}) and secondary liquidity at HVN2 ({ibHvn2}), divided by a low-volume liquidity void at LVN ({ibLvn}). 
Based on this structural state, the algorithm confirms a {tradingSignal} execution logic. The strategic plan dictates an Entry at {entryPrice}, structural invalidation (Stop Loss) at {stopLoss}, and a liquidity target (Take Profit) at {takeProfit}.
";

                // We acquire lock to write to file to prevent concurrent access issues
                lock (_lockObj)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                        File.AppendAllText(filePath, payload + Environment.NewLine + Environment.NewLine);
                        
                        string logPath = @"C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\AI_Global_Events.log";
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [HIT 2: CONSOLIDATED RECAP] {sessionId}" + Environment.NewLine + payload + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to append cognee payload to {filePath}: {ex.Message}");
                    }
                }

                if (enableWebhook)
                {
                    await _memoryThrottle.WaitAsync();
                    try
                    {
                        string safePayload = payload.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
                        string autoCognifyStr = autoCognify ? "true" : "false";
                        string jsonPayload = $"{{\"payload\": \"{safePayload}\", \"auto_cognify\": {autoCognifyStr}}}";
                        
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        await httpClient.PostAsync("http://127.0.0.1:8000/memory", content);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to send Strategy Cognee payload: {ex.Message}");
                    }
                    finally
                    {
                        _memoryThrottle.Release();
                    }
                }
            });
        }
    }
}
