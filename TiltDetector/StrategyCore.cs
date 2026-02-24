using System;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public class StrategyCore(IStrategyContext context)
    {
        private readonly IStrategyLogger _logger =
            context.Logger ?? throw new ArgumentNullException(nameof(context.Logger));

        private double tiltScore;

        public void Start()
        {
            _logger.LogInfo("TiltDetector started");
        }

        public void Stop()
        {
            _logger.LogInfo("TiltDetector stopped");
        }

        public void OnTradeFilled(Trade trade) { }

        public void Decay() { }
    }
}
