using System;
using Moq;
using Xunit;

namespace TiltDetector.Test
{
    public class StrategyCoreTests
    {
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly Mock<IStrategySettings> _settingsMock;
        private readonly Mock<IStrategyContext> _contextMock;
        private readonly StrategyCore _core;

        public StrategyCoreTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();

            _settingsMock = new Mock<IStrategySettings>();

            _contextMock = new Mock<IStrategyContext>();
            _contextMock.SetupGet(c => c.Logger).Returns(_loggerMock.Object);
            _contextMock.SetupGet(c => c.Settings).Returns(_settingsMock.Object);

            _core = new StrategyCore(_contextMock.Object);
        }
    }
}
