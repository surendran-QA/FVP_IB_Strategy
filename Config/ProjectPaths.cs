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
        /// <summary>
        /// Centralized base directory for the strategy's output files.
        /// </summary>
        public static readonly string BaseDirectory = @"C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy";
        
        public static string GetBaseStrategyDirectory()
        {
            return BaseDirectory;
        }
    }
}
