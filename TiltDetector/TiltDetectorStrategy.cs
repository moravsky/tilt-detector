using System;
using System.Collections.Generic;
using System.ComponentModel;
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
                _timeProvider = new CustomTimeProvider(DateTimeOffset.UnixEpoch);

                var context = new StrategyContext(
                    Logger: this,
                    Settings: this,
                    TimeProvider: _timeProvider
                );
                _core = new StrategyCore(context);
                _core.PropertyChanged += Core_PropertyChanged;

                Core.TradeAdded += OnTradeAdded;
                _core.Run();
            }
            catch (Exception ex)
            {
                LogError($"Run() failed: {ex}");
            }
        }

        private void Core_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StrategyCore.IsTradingLocked) && _core != null)
            {
                if (_core.IsTradingLocked)
                {
                    LogInfo(
                        $"Tilt score {_core.TiltScore} above lock threshold {LockThreshold}! Locking platform trading."
                    );
                    Core.Instance.TradingStatus = TradingStatus.Locked;
                }
                else
                {
                    LogInfo(
                        $"Tilt score {_core.TiltScore} decayed below unlock threshold {UnlockThreshold}. Unlocking platform trading."
                    );
                    Core.Instance.TradingStatus = TradingStatus.Allowed;
                }
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

        [Obsolete("Use OnInitializeMetrics()")]
        protected override List<StrategyMetric> OnGetMetrics()
        {
            var result = base.OnGetMetrics();

            try
            {
                result.Add(
                    new StrategyMetric
                    {
                        Name = "Tilt Score",
                        FormattedValue = _core != null ? $"{_core.TiltScore:F2}" : "N/A",
                    }
                );
                result.Add(
                    new StrategyMetric
                    {
                        Name = "Lock Threshold",
                        FormattedValue = $"{LockThreshold:F2}",
                    }
                );
                result.Add(
                    new StrategyMetric
                    {
                        Name = "Unlock Threshold",
                        FormattedValue = $"{UnlockThreshold:F2}",
                    }
                );
                result.Add(
                    new StrategyMetric
                    {
                        Name = "Tilt Decay Halflife (Mins)",
                        FormattedValue = $"{HalfLifeMinutes}",
                    }
                );
            }
            catch (Exception ex)
            {
                LogError($"OnGetMetrics failed: {ex.Message}");
            }

            return result;
        }

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
