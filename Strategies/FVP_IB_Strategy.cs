using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TradingPlatform.BusinessLayer;
using CustomStrategies.Calculations;
using CustomStrategies.Execution;
using FVP_IB_Strategy.Calculations;

namespace CustomStrategies
{
    public partial class FVP_IB_Strategy : Strategy, ICurrentAccount, ICurrentSymbol, IVolumeAnalysisIndicator
    {

        protected override void OnRun()
        {
            this.waitOpenPosition = false; // FLAW FIX: Must initialize to false to allow the first trade to place!
            this.isAwaitingAiScore = false;
            this.totalTradesCount = 0;
            this.totalWins = 0;
            this.totalLosses = 0;
            this.totalNetProfit = 0;
            this.processedPositionIds.Clear();

            EnsureReportsInitialized();

            if (this.CurrentSymbol == null || this.CurrentAccount == null)
            {
                this.Log("Symbol or Account is not specified.", StrategyLoggingLevel.Error);
                return;
            }

            if (this.CurrentSymbol.ConnectionId != this.CurrentAccount.ConnectionId)
            {
                this.Log("Symbol and Account from different connections.", StrategyLoggingLevel.Error);
                return;
            }

            this.orderTypeId = Core.Instance.OrderTypes.FirstOrDefault(x => x.ConnectionId == this.CurrentSymbol.ConnectionId && x.Behavior == OrderTypeBehavior.Limit)?.Id;
            this.marketOrderTypeId = Core.Instance.OrderTypes.FirstOrDefault(x => x.ConnectionId == this.CurrentSymbol.ConnectionId && x.Behavior == OrderTypeBehavior.Market)?.Id;
            if (string.IsNullOrEmpty(this.orderTypeId))
            {
                this.Log("Connection does not support limit orders.", StrategyLoggingLevel.Error);
                return;
            }

            this.atrIndicator = Core.Instance.Indicators.BuiltIn.ATR(14, MaMode.SMA);

            // History loading is moved to OnUpdate to avoid the Quantower Backtester clock bug in OnRun.

            Core.PositionAdded += Core_PositionAdded;
            Core.PositionRemoved += Core_PositionRemoved;

            this.marketData = new MarketData();
            this.ibEngine = new InitialBalanceEngine();
            this.orderManager = new OrderManager();
            this.cogneeService = new CogneeIntegrationService();

            this.tradingContext = new TradingContext
            {
                CurrentAccount = this.CurrentAccount,
                CurrentSymbol = this.CurrentSymbol,
                OrderTypeId = this.orderTypeId,
                Quantity = this.Quantity,
                LogAction = (msg, level) => this.Log(msg, level),
                SetWaitOpenPosition = (val) => this.waitOpenPosition = val
            };
        }

        protected override void OnStop()
        {
            Core.PositionAdded -= Core_PositionAdded;
            Core.PositionRemoved -= Core_PositionRemoved;

            if (this.hdm != null)
            {
                this.hdm.HistoryItemUpdated -= Hdm_HistoryItemUpdated;
                this.hdm.Dispose();
            }

            if (this.cogneeService != null && this.cogneeService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }

            global::FVP_IB_Strategy.Calculations.ReportExporter.FlushReports(this.activeCsvFilePath, this.activeAllSignalsCsvPath);
            this.Log($"Successfully flushed all in-memory report rows to {this.activeCsvFilePath}!", StrategyLoggingLevel.Trading);

            base.OnStop();
        }


    }
}


