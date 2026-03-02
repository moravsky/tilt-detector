using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public partial class TiltDetectorStrategy
        : Strategy,
            IDisposable,
            IStrategySettings,
            IStrategyLogger
    {
        private bool _disposed;
        private StrategyCore? _core;
        private Symbol? _heartbeatSymbol;

        public TiltDetectorStrategy()
        {
            this.Name = "TiltDetector";
            this.Description = "Detects tilt conditions and locks trading";
        }

        protected override void OnRun()
        {
            _core = new StrategyCore(new StrategyContext(this));
            Core.TradeAdded += OnTradeAdded;
            try
            {
                if (Account == null)
                {
                    LogError("Target account not set, cannot continue");
                    return;
                }
                InitializeHeartbeat();
                _core.Run();
            }
            catch (Exception ex)
            {
                LogError($"Run() failed: {ex}");
            }
        }

        public void InitializeHeartbeat()
        {
            var firstSymbol = Core
                .Symbols.Where(s => s.ConnectionId == Account.ConnectionId)
                .FirstOrDefault();

            if (firstSymbol == null)
            {
                throw new ApplicationException(
                    "Couldn't initialize heartbeat, firstSymbol is null"
                );
            }

            _heartbeatSymbol = Core.GetSymbol(firstSymbol.CreateInfo());
        }

        private void OnTradeAdded(Trade Trade)
        {
            if (_disposed)
                return;

            try
            {
                _core.OnTradeAdded(Trade);
            }
            catch (Exception ex)
            {
                LogError($"OnTradeAdded failed: {ex}");
            }
        }

        protected override void OnStop()
        {
            Core.TradeAdded -= OnTradeAdded;
            _core = null;
        }

        protected override void OnRemove() => Dispose();

        public static IEnumerable<Trade> GetTrades(
            TradesHistoryRequestParameters tradesHistoryRequestParameters
        )
        {
            var trades = Core.Instance.GetTrades(tradesHistoryRequestParameters);
            return trades;
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                OnStop();
                base.Dispose();
            }
        }

        public void LogError(string message) => Log(message, StrategyLoggingLevel.Error);

        public void LogInfo(string message) => Log(message, StrategyLoggingLevel.Info);
    }
}
