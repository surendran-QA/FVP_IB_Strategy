using System;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies.Execution
{
    public class OrderManager
    {
        public void ExecuteTrade(
            MarketData marketData, 
            TradingContext context)
        {
            if (marketData.Signal == null || marketData.Signal.PreferredSide == "FADE")
                return;
                
            Side side = marketData.Signal.PreferredSide == "LONG" ? Side.Buy : Side.Sell;

            context.SetWaitOpenPosition?.Invoke(true);
            
            double slPrice = marketData.Signal.StopLoss;
            double tpPrice = marketData.Signal.TakeProfit;
            double entryPrice = marketData.Signal.EntryPrice;

            double currentPrice = marketData.Signal.EntryPrice; // Fallback
            try { currentPrice = context.CurrentSymbol.Last; } catch { }

            OrderTypeBehavior requiredBehavior = OrderTypeBehavior.Limit;
            if (side == Side.Buy && currentPrice < entryPrice)
                requiredBehavior = OrderTypeBehavior.Stop;
            else if (side == Side.Sell && currentPrice > entryPrice)
                requiredBehavior = OrderTypeBehavior.Stop;

            string determinedOrderTypeId = Core.Instance.OrderTypes.FirstOrDefault(x => x.ConnectionId == context.CurrentSymbol.ConnectionId && x.Behavior == requiredBehavior)?.Id ?? context.OrderTypeId;

            var placeOrderReq = new PlaceOrderRequestParameters()
            {
                Account = context.CurrentAccount,
                Symbol = context.CurrentSymbol,
                OrderTypeId = determinedOrderTypeId,
                Quantity = context.Quantity,
                Side = side,
                Price = requiredBehavior == OrderTypeBehavior.Limit ? entryPrice : default(double),
                TriggerPrice = requiredBehavior == OrderTypeBehavior.Stop ? entryPrice : default(double),
                TimeInForce = TimeInForce.Day,
                StopLoss = SlTpHolder.CreateSL(slPrice),
                TakeProfit = SlTpHolder.CreateTP(tpPrice)
            };

            var result = Core.Instance.PlaceOrder(placeOrderReq);

            if (result.Status == TradingOperationResultStatus.Failure)
            {
                context.LogAction?.Invoke($"Place order refuse: {result.Message}", StrategyLoggingLevel.Trading);
                context.SetWaitOpenPosition?.Invoke(false);
            }
            else
            {
                context.LogAction?.Invoke($"Position open: {result.Status}", StrategyLoggingLevel.Trading);
            }
        }
    }
}
