using System;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;

namespace TiltDetector
{
    public partial class TiltDetectorStrategy
    {
        public double HalfLifeMinutes { get; private set; }
        public double LockThreshold { get; private set; }
        public double UnlockThreshold { get; private set; }
    }
}
