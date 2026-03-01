using System;
using System.Linq;
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
        private bool _locked;

        public event Action? TradingLocked;
        public event Action? TradingUnlocked;

        public void Run()
        {
            ArgumentNullException.ThrowIfNull(
                _settings.Account,
                "Target account not set, cannot continue"
            );
            UpdateTiltScore(context.HeartbeatUtc);
        }

        public void OnTradeAdded(Trade trade)
        {
            var tradeTime = trade.DateTime.ToUniversalTime();
            UpdateTiltScore(tradeTime);
        }

        private void UpdateTiltScore(DateTime utcNow)
        {
            TiltScore = 0;
            var tradesHistoryRequestParameters = new TradesHistoryRequestParameters()
            {
                From = utcNow.AddMinutes(-_settings.HalfLifeMinutes * MaxHalfLives).ToLocalTime(),
                To = utcNow.AddSeconds(10).ToLocalTime(),
            };

            var trades = context
                .GetTrades(tradesHistoryRequestParameters)
                .Where(t => t.Account.Id == _settings.Account.Id);

            TiltScore = trades.Where(t => t.GrossPnl?.Value < 0).Sum(t => GetImpact(t, utcNow));
            if (!_locked && TiltScore >= _settings.LockThreshold)
            {
                _locked = true;
                TradingLocked?.Invoke();
            }
            else if (_locked && TiltScore <= _settings.UnlockThreshold)
            {
                _locked = false;
                TradingUnlocked?.Invoke();
            }
        }

        private double GetImpact(Trade trade, DateTime now)
        {
            var tradeTime = trade.DateTime.ToUniversalTime();
            var ageMilliseconds = Math.Max(0, (now - tradeTime).TotalMilliseconds);
            var weight = Math.Pow(2, -ageMilliseconds / (_settings.HalfLifeMinutes * 60_000.0));
            var impact = -trade.GrossPnl.Value * weight;
            return impact;
        }
    }
}
