using System;
using CustomStrategies.Models;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FVP_IB_Strategy.Calculations
{
    public interface ICogneeIntegrationService
    {
        Task<double> AnalyzeSetupAsync(PayloadContext ctx);

        void AppendCogneePayload(
            string filePath, string strategyName, string assetName, DateTime barTime,
            string ibShape, string tradingSignal, string tradeResult,
            string exitReason, string ibHvn1, string ibHvn2,
            string ibLvn, double ibHigh, double ibLow,
            double ibPoc, double ibVah, double ibVal,
            double totalVolume, double entryPrice, double stopLoss,
            double takeProfit, double sessionHigh, double sessionLow,
            double nyOpenPrice, bool enableWebhook, bool autoCognify);
    }

    public class CogneeIntegrationService : ICogneeIntegrationService, IDisposable
    {
        private readonly object _lockObj = new object();
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _analyzeThrottle;
        private readonly SemaphoreSlim _memoryThrottle;

        public CogneeIntegrationService()
        {
            _httpClient = new HttpClient();
            _analyzeThrottle = new SemaphoreSlim(1, 1);
            _memoryThrottle = new SemaphoreSlim(1, 1);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _analyzeThrottle?.Dispose();
            _memoryThrottle?.Dispose();
        }

        public async Task<double> AnalyzeSetupAsync(PayloadContext ctx)
        {
            string date = ctx.EstTime.ToString("yyyy-MM-dd");
            string time = ctx.EstTime.ToString("HH:mm:ss");
            string dayOfWeek = ctx.EstTime.DayOfWeek.ToString();
            string sessionId = $"{ctx.Symbol}_{date}";

            double ibRange = ctx.IbHigh - ctx.IbLow;
            string pocPct = ibRange > 0 ? ((ctx.IbPoc - ctx.IbLow) / ibRange * 100).ToString("F1") + "%" : "N/A";
            string vahPct = ibRange > 0 ? ((ctx.IbVah - ctx.IbLow) / ibRange * 100).ToString("F1") + "%" : "N/A";
            string valPct = ibRange > 0 ? ((ctx.IbVal - ctx.IbLow) / ibRange * 100).ToString("F1") + "%" : "N/A";

            string assetGroup = ctx.Symbol;
            if (ctx.Symbol.StartsWith("MNQ") || ctx.Symbol.StartsWith("NQ")) assetGroup = "NQ";
            else if (ctx.Symbol.StartsWith("MES") || ctx.Symbol.StartsWith("ES")) assetGroup = "ES";
            else if (ctx.Symbol.StartsWith("MGC") || ctx.Symbol.StartsWith("GC")) assetGroup = "GC";
            else if (ctx.Symbol.StartsWith("M2K") || ctx.Symbol.StartsWith("RTY")) assetGroup = "RTY";
            else if (ctx.Symbol.StartsWith("MYM") || ctx.Symbol.StartsWith("YM")) assetGroup = "YM";

            string payload = $@"
[SESSION ID: {sessionId}]
[MARKET CONTEXT NODE]
Strategy: {ctx.StrategyName}
Event Tag: PRE_TRADE: Signal Generated
Day of Week: {dayOfWeek}
Asset Group: {assetGroup}
Asset: {ctx.Symbol}
Timestamp: {date} {time}

[STRUCTURAL STATE NODE]
Profile Shape: {ctx.Shape}
Total Session Volume: {ctx.TotalVolume}
Session Extremes: IB_High {ctx.IbHigh} | IB_Low {ctx.IbLow}
Value Area: VAH {ctx.IbVah} ({vahPct}) | POC {ctx.IbPoc} ({pocPct}) | VAL {ctx.IbVal} ({valPct})
Microstructure: HVN1 {ctx.IbHvn1} | HVN2 {ctx.IbHvn2} | LVN_Gap {ctx.IbLvn}
";

            // Global Logging
            lock (_lockObj)
            {
                try
                {
                    string logPath = global::FVP_IB_Strategy.Config.ProjectPaths.GetLogFilePath();
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PAYLOAD 1 SENT] [HIT 1: MORNING SETUP] {sessionId}" + Environment.NewLine + payload + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
                }
            }

            if (!ctx.EnableWebhook) return 50.0;

            bool lockAcquired = false;
            try
            {
                lockAcquired = await _analyzeThrottle.WaitAsync(TimeSpan.FromSeconds(5));
                if (!lockAcquired)
                {
                    // C1 FAIL-CLOSED: could not even send the request. Return a sub-threshold
                    // sentinel (<40) so the caller VETOES rather than trading blind on 50.
                    System.Diagnostics.Debug.WriteLine("Failed to acquire analyze throttle lock. Skipping analysis.");
                    return -1.0;
                }

                var requestBody = new {
                    payload = payload,
                    fields = new {
                        session_id = sessionId,
                        symbol = ctx.Symbol,
                        ib_high = ctx.IbHigh,
                        ib_low = ctx.IbLow,
                        ib_poc = ctx.IbPoc,
                        ib_vah = ctx.IbVah,
                        ib_val = ctx.IbVal,
                        ib_hvn1 = ctx.IbHvn1,
                        ib_hvn2 = ctx.IbHvn2,
                        ib_lvn = ctx.IbLvn
                    }
                };
                string jsonPayload = System.Text.Json.JsonSerializer.Serialize(requestBody);

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var responseMessage = await _httpClient.PostAsync("http://127.0.0.1:8000/analyze", content);
                string responseStr = await responseMessage.Content.ReadAsStringAsync();

                // Pure Service Layer JSON Parsing Extraction
                double winProb = 50.0; 
                try 
                {
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(responseStr))
                    {
                        if (doc.RootElement.TryGetProperty("error_flag", out var errElement) && errElement.GetBoolean())
                        {
                            // Internal API Fallback engaged
                        }
                        else if (doc.RootElement.TryGetProperty("confidence_score", out var probElement))
                        {
                            string probStr = probElement.GetString()?.Replace("%", "").Trim();
                            if (double.TryParse(probStr, out double parsedProb))
                            {
                                winProb = parsedProb;
                            }
                        }
                        else if (doc.RootElement.TryGetProperty("win_probability", out var oldProbElement))
                        {
                            string probStr = oldProbElement.GetString()?.Replace("%", "").Trim();
                            if (double.TryParse(probStr, out double parsedProb))
                            {
                                winProb = parsedProb;
                            }
                        }
                    }
                } 
                catch 
                {
                    // Parsing failure falls back to 50% seamlessly
                }
                
                return winProb;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to analyze setup: {ex.Message}");
                return 50.0;
            }
            finally
            {
                if (lockAcquired)
                {
                    _analyzeThrottle.Release();
                }
            }
        }

        public void AppendCogneePayload(
            string filePath,
            string strategyName,
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
            double totalVolume,
            double entryPrice,
            double stopLoss,
            double takeProfit,
            double sessionHigh,
            double sessionLow,
            double nyOpenPrice,
            bool enableWebhook,
            bool autoCognify)
        {
            Task.Run(async () =>
            {
                string date = barTime.ToString("yyyy-MM-dd");
                string time = barTime.ToString("HH:mm:ss");
                string dayOfWeek = barTime.DayOfWeek.ToString();
                string sessionId = $"{assetName}_{date}";

                double ibRange = ibHigh - ibLow;
                string pocPct = ibRange > 0 ? ((ibPoc - ibLow) / ibRange * 100).ToString("F1") + "%" : "N/A";
                string vahPct = ibRange > 0 ? ((ibVah - ibLow) / ibRange * 100).ToString("F1") + "%" : "N/A";
                string valPct = ibRange > 0 ? ((ibVal - ibLow) / ibRange * 100).ToString("F1") + "%" : "N/A";

                string mappedEventTag = tradeResult;
                if (tradeResult == "TP Hit") mappedEventTag = "POST_TRADE: Target Achieved";
                else if (tradeResult == "SL Hit") mappedEventTag = "POST_TRADE: Stop Loss Triggered";
                else if (tradeResult == "EOD Flatten") mappedEventTag = "POST_TRADE: EOD Flatten";
                else if (tradeResult == "AI Override") mappedEventTag = "VETO: AI Overridden";
                else if (tradeResult == "Pending Cancelled" || tradeResult == "Not Triggered") mappedEventTag = "TRADE_CANCELLED: Entry Not Triggered";

                string assetGroup = assetName;
                if (assetName.StartsWith("MNQ") || assetName.StartsWith("NQ")) assetGroup = "NQ";
                else if (assetName.StartsWith("MES") || assetName.StartsWith("ES")) assetGroup = "ES";
                else if (assetName.StartsWith("MGC") || assetName.StartsWith("GC")) assetGroup = "GC";
                else if (assetName.StartsWith("M2K") || assetName.StartsWith("RTY")) assetGroup = "RTY";
                else if (assetName.StartsWith("MYM") || assetName.StartsWith("YM")) assetGroup = "YM";

                string runawayText = "";
                if (mappedEventTag == "TRADE_CANCELLED: Entry Not Triggered" && !double.IsNaN(nyOpenPrice))
                {
                    bool isLong = tradingSignal.Contains("LONG") || tradingSignal.Contains("BUY");
                    double extPrice = isLong ? sessionHigh : sessionLow;
                    double extPct = Math.Round(((extPrice - nyOpenPrice) / nyOpenPrice) * 100, 2);
                    
                    double ibExtPts = Math.Round(isLong ? (sessionHigh - ibHigh) : (ibLow - sessionLow), 2);
                    double ibExtPct = Math.Round(isLong ? (ibExtPts / ibHigh * 100) : (ibExtPts / ibLow * 100), 2);
                    
                    runawayText = $" Note: The entry was never triggered. The market trended away, reaching a Session Extreme of {extPrice}, which is a {(extPct > 0 ? "+" : "")}{extPct}% extension from the NY Open, extending {ibExtPts} pts (+{ibExtPct}%) beyond the IB boundary.";
                }

                string payload = $@"
[SESSION ID: {sessionId}]
[MARKET CONTEXT NODE]
Strategy: {strategyName}
Event Tag: {mappedEventTag}
Day of Week: {dayOfWeek}
Asset Group: {assetGroup}
Asset: {assetName}
Timestamp: {date} {time}

[STRUCTURAL STATE NODE]
Profile Shape: {ibShape}
Total Session Volume: {totalVolume}
Session Extremes: IB_High {ibHigh} | IB_Low {ibLow}
Value Area: VAH {ibVah} ({vahPct}) | POC {ibPoc} ({pocPct}) | VAL {ibVal} ({valPct})
Microstructure: HVN1 {ibHvn1} | HVN2 {ibHvn2} | LVN_Gap {ibLvn}

[EXECUTION PLAN NODE]
System Bias: {tradingSignal}
Order Setup: Entry {entryPrice} | Take Profit {takeProfit} | Stop Loss {stopLoss}

[OUTCOME NODE]
Result: {tradeResult}
Exit Reason: {exitReason}

[RELATIONAL SUMMARY]
At {time} on {dayOfWeek}, {date}, {assetName} established its Initial Balance, resolving into a {ibShape} distribution with a total volume of {totalVolume}. 
Primary institutional value is anchored at the POC of {ibPoc} ({pocPct} up from low), contained within the VAH ({ibVah}) and VAL ({ibVal}) boundaries. Session liquidity extremes are marked at a High of {ibHigh} and a Low of {ibLow}. 
Microstructure analysis dictates primary liquidity sitting at HVN1 ({ibHvn1}) and secondary liquidity at HVN2 ({ibHvn2}), divided by a low-volume liquidity void at LVN ({ibLvn}). 
Based on this structural state, the algorithm confirms a {tradingSignal} execution logic. The strategic plan dictates an Entry at {entryPrice}, structural invalidation (Stop Loss) at {stopLoss}, and a liquidity target (Take Profit) at {takeProfit}.
The outcome of the setup was a {tradeResult} due to {exitReason}.{runawayText}
";

                // We acquire lock to write to file to prevent concurrent access issues
                lock (_lockObj)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                        
                        string logPath = global::FVP_IB_Strategy.Config.ProjectPaths.GetLogFilePath();
                        
                        // If the calling file (Indicator) passes the logPath as the payload path, 
                        // don't write the payload twice to the same file.
                        if (filePath != logPath)
                        {
                            File.AppendAllText(filePath, payload + Environment.NewLine + Environment.NewLine);
                        }

                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PAYLOAD 2 SENT] [HIT 2: CONSOLIDATED RECAP] {sessionId}" + Environment.NewLine + payload + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to append cognee payload to {filePath}: {ex.Message}");
                    }
                }

                if (enableWebhook)
                {
                    bool lockAcquired = false;
                    try
                    {
                        lockAcquired = await _memoryThrottle.WaitAsync(TimeSpan.FromSeconds(5));
                        if (!lockAcquired)
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to acquire memory throttle lock. Skipping memory append.");
                            return;
                        }

                        var requestBody = new {
                            payload = payload,
                            auto_cognify = autoCognify,
                            fields = new {
                                session_id = sessionId,
                                symbol = assetName,
                                event_tag = mappedEventTag,
                                ib_high = ibHigh,
                                ib_low = ibLow,
                                ib_poc = ibPoc,
                                ib_vah = ibVah,
                                ib_val = ibVal,
                                total_volume = totalVolume,
                                entry_price = entryPrice,
                                take_profit = takeProfit,
                                stop_loss = stopLoss,
                                trade_result = tradeResult,
                                exit_reason = exitReason
                            }
                        };
                        string jsonPayload = System.Text.Json.JsonSerializer.Serialize(requestBody);

                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        await _httpClient.PostAsync("http://127.0.0.1:8000/memory", content);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to send Strategy Cognee payload: {ex.Message}");
                    }
                    finally
                    {
                        if (lockAcquired)
                        {
                            _memoryThrottle.Release();
                        }
                    }
                }
            });
        }
    }
}
