using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public interface IStrategyLogger
    {
        void LogError(string message);
        void LogInfo(string message);
    }

    public interface IStrategySettings
    {
        Account Account { get; }
        double HalfLifeMinutes { get; }
        double LockThreshold { get; }
        double UnlockThreshold { get; }
    }

    public interface IStrategyContext
    {
        IStrategyLogger Logger { get; }
        IStrategySettings Settings { get; }
        TimeProvider TimeProvider { get; }
        IEnumerable<Trade> GetTrades(TradesHistoryRequestParameters tradesHistoryRequestParameters);
    }

    public record StrategyContext(
        IStrategyLogger Logger,
        IStrategySettings Settings,
        TimeProvider? TimeProvider = null
    ) : IStrategyContext
    {
        private readonly TiltDetectorStrategy _tiltDetectorStrategy;

        public StrategyContext(TiltDetectorStrategy tiltDetectorStrategy)
            : this(Logger: tiltDetectorStrategy, Settings: tiltDetectorStrategy)
        {
            _tiltDetectorStrategy = tiltDetectorStrategy;
        }

        // Fallback to the real system clock if not explicitly provided
        TimeProvider IStrategyContext.TimeProvider => TimeProvider ?? TimeProvider.System;

        public IEnumerable<Trade> GetTrades(
            TradesHistoryRequestParameters tradesHistoryRequestParameters
        )
        {
            return TiltDetectorStrategy.GetTrades(tradesHistoryRequestParameters);
        }
    }
}
