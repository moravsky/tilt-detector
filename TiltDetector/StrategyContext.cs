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
        double HalfLifeMinutes { get; }
        double LockThreshold { get; }
        double UnlockThreshold { get; }
    }

    public interface IStrategyContext
    {
        IStrategyLogger Logger { get; }
        IStrategySettings Settings { get; }
    }

    public record StrategyContext(IStrategyLogger Logger, IStrategySettings Settings)
        : IStrategyContext
    {
        public StrategyContext(TiltDetectorStrategy tiltDetectorStrategy)
            : this(Logger: tiltDetectorStrategy, Settings: tiltDetectorStrategy) { }
    }
}
