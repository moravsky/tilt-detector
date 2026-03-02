using System;
using System.Linq;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public class StrategyCore(IStrategyContext context)
    {
        private readonly IStrategyLogger _logger =
            context.Logger ?? throw new ArgumentNullException(nameof(context.Logger));
        private readonly IStrategySettings _settings =
            context.Settings ?? throw new ArgumentNullException(nameof(context.Settings));
        private const int MaxHalfLives = 7;

        public double TiltScore { get; private set; }
        private bool _tradingLocked;
        public event Action? TradingLocked;
        public event Action? TradingUnlocked;

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
            // Using named arguments for maximum readability
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
                dueTime: TimeSpan.Zero, // How long to wait before the FIRST tick
                period: DecayTimerInterval // How long to wait between ALL SUBSEQUENT ticks
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
            if (!_tradingLocked && TiltScore >= _settings.LockThreshold)
            {
                _tradingLocked = true;
                TradingLocked?.Invoke();
            }
            else if (_tradingLocked && TiltScore <= _settings.UnlockThreshold)
            {
                _tradingLocked = false;
                TradingUnlocked?.Invoke();
            }
        }
    }
}
