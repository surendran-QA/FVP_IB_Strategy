using System;
using System.IO;
using TradingPlatform.BusinessLayer;

namespace FVP_IB_Strategy.Config
{
    public static class ProjectPaths
    {
        /// <summary>
        /// Dynamically retrieves the base directory for the strategy's output files.
        /// This ensures the path works across different Quantower installations.
        /// </summary>
        public static string GetBaseStrategyDirectory()
        {

            try
            {
                // ATTEMPT 2: Executing Assembly Location Fallback with Aggressive Stripping
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exePath))
                {
                    string dir = Path.GetDirectoryName(exePath);
                    // Strip \Compiled if Quantower shadow-copied it
                    int compiledIdx = dir.IndexOf(@"\Settings\Scripts\Compiled", StringComparison.OrdinalIgnoreCase);
                    if (compiledIdx > 0)
                    {
                        dir = dir.Substring(0, compiledIdx);
                        return Path.Combine(dir, "Settings", "Scripts", "Strategies", "FVP_IB_Strategy");
                    }
                }
            }
            catch { /* Ignore reflection errors */ }

            try
            {
                // ATTEMPT 3: AppDomain BaseDirectory with Aggressive Stripping
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                int compiledIdx2 = baseDir.IndexOf(@"\Settings\Scripts\Compiled", StringComparison.OrdinalIgnoreCase);
                if (compiledIdx2 > 0)
                {
                    baseDir = baseDir.Substring(0, compiledIdx2);
                }
                
                return Path.Combine(baseDir, "Settings", "Scripts", "Strategies", "FVP_IB_Strategy");
            }
            catch
            {
                // FINAL FALLBACK
                return @"C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy";
            }
        }
        
        public static string GetLogFilePath()
        {
            return Path.Combine(GetBaseStrategyDirectory(), "quant_engine.log");
        }
    }
}
