using System;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public class StrategyCore(IStrategyContext context)
    {
        private readonly IStrategyLogger _logger =
            context.Logger ?? throw new ArgumentNullException(nameof(context.Logger));

        public double TiltScore { get; private set; }

        public event Action? TradingLocked;
        public event Action? TradingUnlocked;

        public void OnTradeAdded(Trade trade) { }
    }
}
