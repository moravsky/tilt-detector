using System;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public partial class TiltDetectorStrategy
    {
        [InputParameter]
        public Account Account { get; private set; }

        [InputParameter]
        public double HalfLifeMinutes { get; private set; } = 30.0;

        [InputParameter]
        public double LockThreshold { get; private set; } = 2000.0;

        [InputParameter]
        public double UnlockThreshold { get; private set; } = 1500.0;
    }
}
