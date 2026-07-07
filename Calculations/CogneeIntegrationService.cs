using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FVP_IB_Strategy.Calculations
{
    public interface ICogneeIntegrationService
    {
        Task<string> AnalyzeSetupAsync(
            string strategyName, string assetName, DateTime barTime, string ibShape,
            string ibHvn1, string ibHvn2, string ibLvn,
            double ibHigh, double ibLow, double ibPoc,
            double ibVah, double ibVal, double totalVolume,
            bool enableWebhook);

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
        private readonly SemaphoreSlim _memoryThrottle;

        public CogneeIntegrationService()
        {
            _httpClient = new HttpClient();
            _memoryThrottle = new SemaphoreSlim(1, 1);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _memoryThrottle?.Dispose();
        }

        public async Task<string> AnalyzeSetupAsync(
            string strategyName,
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
            double totalVolume,
            bool enableWebhook)
        {
            string date = barTime.ToString("yyyy-MM-dd");
            string time = barTime.ToString("HH:mm:ss");
            string dayOfWeek = barTime.DayOfWeek.ToString();
            string sessionId = $"{assetName}_{date}";

            double ibRange = ibHigh - ibLow;
            string pocPct = ibRange > 0 ? ((ibPoc - ibLow) / ibRange * 100).ToString("F1") + "%" : "N/A";
            string vahPct = ibRange > 0 ? ((ibVah - ibLow) / ibRange * 100).ToString("F1") + "%" : "N/A";
            string valPct = ibRange > 0 ? ((ibVal - ibLow) / ibRange * 100).ToString("F1") + "%" : "N/A";

            string assetGroup = assetName;
            if (assetName.StartsWith("MNQ") || assetName.StartsWith("NQ")) assetGroup = "NQ";
            else if (assetName.StartsWith("MES") || assetName.StartsWith("ES")) assetGroup = "ES";
            else if (assetName.StartsWith("MGC") || assetName.StartsWith("GC")) assetGroup = "GC";
            else if (assetName.StartsWith("M2K") || assetName.StartsWith("RTY")) assetGroup = "RTY";
            else if (assetName.StartsWith("MYM") || assetName.StartsWith("YM")) assetGroup = "YM";

            string payload = $@"
[SESSION ID: {sessionId}]
[MARKET CONTEXT NODE]
Strategy: {strategyName}
Event Tag: PRE_TRADE: Signal Generated
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
";

            // Global Logging
            lock (_lockObj)
            {
                try
                {
                    string logPath = global::FVP_IB_Strategy.Config.ProjectPaths.GetLogFilePath();
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PAYLOAD 1 SENT] [HIT 1: MORNING SETUP] {sessionId}" + Environment.NewLine + payload + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
                }
            }

            if (!enableWebhook) return "{\"ai_score\": \"50\", \"win_probability\": \"50%\", \"narrative\": \"Webhook Disabled\"}";

            bool lockAcquired = false;
            try
            {
                lockAcquired = await _memoryThrottle.WaitAsync(TimeSpan.FromSeconds(5));
                if (!lockAcquired)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to acquire memory throttle lock. Skipping analysis.");
                    return "{\"ai_score\": \"50\", \"win_probability\": \"50%\", \"narrative\": \"Lock Timeout\"}";
                }

                string safePayload = payload.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
                string jsonPayload = $"{{\"payload\": \"{safePayload}\"}}";

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("http://127.0.0.1:8000/analyze", content);
                string responseStr = await response.Content.ReadAsStringAsync();

                // Return the raw JSON directly to the caller for strictly-typed parsing
                return responseStr;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to analyze setup: {ex.Message}");
                return $"{{\"ai_score\": \"50\", \"win_probability\": \"50%\", \"narrative\": \"Error - {ex.Message}\"}}";
            }
            finally
            {
                if (lockAcquired)
                {
                    _memoryThrottle.Release();
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

                        string safePayload = payload.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
                        string autoCognifyStr = autoCognify ? "true" : "false";
                        string jsonPayload = $"{{\"payload\": \"{safePayload}\", \"auto_cognify\": {autoCognifyStr}}}";

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
