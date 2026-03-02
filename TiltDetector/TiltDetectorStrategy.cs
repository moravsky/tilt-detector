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

        // Add our custom time engine
        private CustomTimeProvider? _timeProvider;

        public TiltDetectorStrategy()
        {
            this.Name = "TiltDetector";
            this.Description = "Detects tilt conditions and locks trading";
        }

        protected override void OnRun()
        {
            try
            {
                if (Account == null)
                {
                    LogError("Target account not set, cannot continue");
                    return;
                }

                InitializeHeartbeat();
                _timeProvider = new CustomTimeProvider(DateTimeOffset.MinValue);

                var context = new StrategyContext(
                    Logger: this,
                    Settings: this,
                    TimeProvider: _timeProvider
                );
                _core = new StrategyCore(context);

                Core.TradeAdded += OnTradeAdded;
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
            _heartbeatSymbol.NewQuote += OnNewQuote;
        }

        private void OnNewQuote(Symbol symbol, Quote quote)
        {
            if (_disposed || _timeProvider == null)
                return;

            var quoteTime = quote.Time.ToUniversalTime();
            _timeProvider.Advance(quoteTime);
        }

        private void OnTradeAdded(Trade Trade)
        {
            if (_disposed)
                return;

            try
            {
                _core?.OnTradeAdded(Trade);
            }
            catch (Exception ex)
            {
                LogError($"OnTradeAdded failed: {ex}");
            }
        }

        protected override void OnStop()
        {
            Core.TradeAdded -= OnTradeAdded;

            // Clean up the event subscription to prevent memory leaks
            if (_heartbeatSymbol != null)
            {
                _heartbeatSymbol.NewQuote -= OnNewQuote;
            }

            // Dispose the core (which drops the timer)
            if (_core is IDisposable disposableCore)
            {
                disposableCore.Dispose();
            }

            _core = null;
            _timeProvider = null;
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
