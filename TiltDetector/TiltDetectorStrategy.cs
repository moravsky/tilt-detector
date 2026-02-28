using System;
using System.Collections.Generic;
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

        public TiltDetectorStrategy()
        {
            this.Name = "TiltDetector";
            this.Description = "Detects tilt conditions and locks trading";
        }

        protected override void OnRun()
        {
            _core = new StrategyCore(new StrategyContext(this));
            Core.TradeAdded += OnTradeAdded;
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
                Log($"OnTradeAdded failed: {ex}", StrategyLoggingLevel.Error);
            }
        }

        protected override void OnStop()
        {
            Core.TradeAdded -= OnTradeAdded;
            _core = null;
        }

        protected override void OnRemove() => Dispose();

        public static DateTime UtcNow => Core.Instance.TimeUtils.DateTimeUtcNow;

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
