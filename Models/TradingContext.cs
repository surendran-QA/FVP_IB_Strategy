using System;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies
{
    public class TradingContext
    {
        public Account CurrentAccount { get; set; }
        public Symbol CurrentSymbol { get; set; }
        public string OrderTypeId { get; set; }
        public double Quantity { get; set; }
        

        
        public Action<string, StrategyLoggingLevel> LogAction { get; set; }
        public Action<bool> SetWaitOpenPosition { get; set; }
    }
}
