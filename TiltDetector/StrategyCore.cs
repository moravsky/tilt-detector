using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public class StrategyCore(IStrategyContext context) : INotifyPropertyChanged, IDisposable
    {
        private readonly IStrategyLogger _logger =
            context.Logger ?? throw new ArgumentNullException(nameof(context.Logger));
        private readonly IStrategySettings _settings =
            context.Settings ?? throw new ArgumentNullException(nameof(context.Settings));
        private const int MaxHalfLives = 7;
        private bool _disposed = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(
            ref T backingStore,
            T value,
            [CallerMemberName] string propertyName = ""
        )
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private double _tiltScore;
        public double TiltScore
        {
            get => _tiltScore;
            private set => SetProperty(ref _tiltScore, value);
        }

        private bool _isTradingLocked;
        public bool IsTradingLocked
        {
            get => _isTradingLocked;
            private set => SetProperty(ref _isTradingLocked, value);
        }

        private static readonly TimeSpan DecayTimerInterval = TimeSpan.FromMinutes(1);
        private ITimer? _decayTimer;
        private readonly object _lock = new();

        public void Run()
        {
            ArgumentNullException.ThrowIfNull(
                _settings.Account,
                "Target account not set, cannot continue"
            );

            var now = context.TimeProvider.GetUtcNow().UtcDateTime;

            _decayTimer = context.TimeProvider.CreateTimer(
                callback: _ =>
                {
                    try
                    {
                        UpdateTiltScore(context.TimeProvider.GetUtcNow().UtcDateTime);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Decay timer failed: {ex.Message}");
                    }
                },
                state: null,
                dueTime: TimeSpan.Zero,
                period: DecayTimerInterval
            );
        }

        public void OnTradeAdded(Trade trade)
        {
            var tradeTime = trade.DateTime.ToUniversalTime();
            UpdateTiltScore(tradeTime);
        }

        private void UpdateTiltScore(DateTime utcNow)
        {
            TiltScore = GetTiltScore(utcNow);
            UpdateTradingState();
        }

        private double GetTiltScore(DateTime utcNow)
        {
            var tradesHistoryRequestParameters = new TradesHistoryRequestParameters()
            {
                From = utcNow.AddMinutes(-_settings.HalfLifeMinutes * MaxHalfLives).ToLocalTime(),
                To = utcNow.AddSeconds(10).ToLocalTime(),
            };

            var trades = context
                .GetTrades(tradesHistoryRequestParameters)
                .Where(t => t.Account.Id == _settings.Account.Id);

            var tiltScore = trades.Where(t => t.GrossPnl?.Value < 0).Sum(t => GetImpact(t, utcNow));
            return tiltScore;
        }

        private double GetImpact(Trade trade, DateTime now)
        {
            var tradeTime = trade.DateTime.ToUniversalTime();
            var ageMilliseconds = Math.Max(0, (now - tradeTime).TotalMilliseconds);
            var weight = Math.Pow(2, -ageMilliseconds / (_settings.HalfLifeMinutes * 60_000.0));
            var impact = -trade.GrossPnl.Value * weight;
            return impact;
        }

        private void UpdateTradingState()
        {
            if (TiltScore >= _settings.LockThreshold)
            {
                IsTradingLocked = true;
            }
            else if (TiltScore <= _settings.UnlockThreshold)
            {
                IsTradingLocked = false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _decayTimer?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
