using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace TiltDetector.Test
{
    public class TiltMonitorTests
    {
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly Mock<IStrategySettings> _settingsMock;
        private readonly Mock<IStrategyContext> _contextMock;

        private const string AccountId = "acc1";
        private const double HalfLifeMinutes = 30.0;
        private const double LockThreshold = 2000.0;
        private const double UnlockThreshold = 1500.0;

        private readonly Account _account;
        private readonly DateTime _now = new DateTime(
            year: 2026,
            month: 3,
            day: 1,
            hour: 12,
            minute: 0,
            second: 0,
            kind: DateTimeKind.Utc
        );
        private readonly CustomTimeProvider _timeProvider;

        public TiltMonitorTests()
        {
            _account = QuantowerTestFactory.CreateAccount(AccountId);

            _loggerMock = new Mock<IStrategyLogger>();

            _settingsMock = new Mock<IStrategySettings>();
            _settingsMock.SetupGet(s => s.Account).Returns(_account);
            _settingsMock.SetupGet(s => s.HalfLifeMinutes).Returns(HalfLifeMinutes);
            _settingsMock.SetupGet(s => s.LockThreshold).Returns(LockThreshold);
            _settingsMock.SetupGet(s => s.UnlockThreshold).Returns(UnlockThreshold);

            _timeProvider = new CustomTimeProvider(_now);

            _contextMock = new Mock<IStrategyContext>();
            _contextMock.SetupGet(c => c.Logger).Returns(_loggerMock.Object);
            _contextMock.SetupGet(c => c.Settings).Returns(_settingsMock.Object);
            _contextMock.SetupGet(c => c.TimeProvider).Returns(_timeProvider);
            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns([]);
        }

        private TiltMonitor CreateTiltMonitor(IEnumerable<Trade> trades)
        {
            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns(trades);
            return new TiltMonitor(_contextMock.Object);
        }

        private Trade MakeLoss(DateTime utcTime, double loss) =>
            QuantowerTestFactory.CreateTrade(utcTime, -Math.Abs(loss), _account);

        private Trade MakeWin(DateTime utcTime, double profit) =>
            QuantowerTestFactory.CreateTrade(utcTime, Math.Abs(profit), _account);

        private Trade MakeLossOnOtherAccount(DateTime utcTime, double loss) =>
            QuantowerTestFactory.CreateTrade(
                utcTime,
                -Math.Abs(loss),
                QuantowerTestFactory.CreateAccount("other_account")
            );

        [Fact]
        public void TiltScore_IsZero_WhenNoTrades()
        {
            var monitor = CreateTiltMonitor([]);
            monitor.Run();

            Assert.Equal(0, monitor.TiltScore);
        }

        [Fact]
        public void TiltScore_IsZero_WhenOnlyWinningTrades()
        {
            var monitor = CreateTiltMonitor([MakeWin(_now.AddMinutes(-5), 100)]);
            monitor.Run();

            Assert.Equal(0, monitor.TiltScore);
        }

        [Theory]
        [InlineData(0, 100.0)] // 0 half-lives = full weight
        [InlineData(1, 50.0)] // 1 half-life = 50% weight
        [InlineData(2, 25.0)] // 2 half-lives = 25% weight
        public void TiltScore_LoweredCorrectly_BasedOnHalfLife(
            double halfLivesElapsed,
            double expectedScore
        )
        {
            var tradeTime = _now.AddMinutes(-HalfLifeMinutes * halfLivesElapsed);
            var monitor = CreateTiltMonitor([MakeLoss(tradeTime, 100)]);

            monitor.Run();
            Assert.Equal(expectedScore, monitor.TiltScore, precision: 6);
        }

        [Fact]
        public void TiltScore_SumsMultipleLosses()
        {
            var trades = new[] { MakeLoss(_now, 100), MakeLoss(_now, 50) };
            var monitor = CreateTiltMonitor(trades);
            monitor.Run();

            Assert.Equal(150.0, monitor.TiltScore, precision: 6);
        }

        [Fact]
        public void TiltScore_IgnoresTrades_FromOtherAccounts()
        {
            var monitor = CreateTiltMonitor([MakeLossOnOtherAccount(_now, 500)]);
            monitor.Run();

            Assert.Equal(0, monitor.TiltScore);
        }

        [Fact]
        public void TiltScore_UpdatesOnTradeAdded()
        {
            var monitor = CreateTiltMonitor([]);
            monitor.Run();
            Assert.Equal(0, monitor.TiltScore);

            var newTrade = MakeLoss(_now, 100);
            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns([newTrade]);

            monitor.OnTradeAdded(newTrade);

            Assert.Equal(100.0, monitor.TiltScore, precision: 6);
        }

        [Fact]
        public void TradingLocked_Fires_WhenScoreCrossesLockThreshold()
        {
            var monitor = CreateTiltMonitor([]);
            monitor.Run();

            bool locked = false;
            monitor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TiltMonitor.IsTradingLocked))
                    locked = monitor.IsTradingLocked;
            };

            var bigLoss = MakeLoss(_now, LockThreshold + 1);
            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns([bigLoss]);

            monitor.OnTradeAdded(bigLoss);

            Assert.True(locked);
        }

        [Fact]
        public void TradingLocked_DoesNotFire_WhenAlreadyLocked()
        {
            var bigLoss = MakeLoss(_now, LockThreshold + 1);
            var monitor = CreateTiltMonitor([bigLoss]);
            monitor.Run();

            int lockEventCount = 0;
            monitor.PropertyChanged += (s, e) =>
            {
                if (
                    e.PropertyName == nameof(TiltMonitor.IsTradingLocked)
                    && monitor.IsTradingLocked
                )
                    lockEventCount++;
            };

            monitor.OnTradeAdded(bigLoss);

            Assert.Equal(0, lockEventCount);
        }

        [Fact]
        public void TradingUnlocked_Fires_WhenScoreDropsBelowUnlockThreshold()
        {
            var bigLoss = MakeLoss(_now, LockThreshold + 1);
            var monitor = CreateTiltMonitor([bigLoss]);
            monitor.Run();

            bool unlocked = false;
            monitor.PropertyChanged += (s, e) =>
            {
                if (
                    e.PropertyName == nameof(TiltMonitor.IsTradingLocked)
                    && !monitor.IsTradingLocked
                )
                    unlocked = true;
            };

            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns([]);

            monitor.OnTradeAdded(MakeWin(_now, 1));

            Assert.True(unlocked);
        }

        [Fact]
        public void TradingUnlocked_DoesNotFire_WhenNotLocked()
        {
            var monitor = CreateTiltMonitor([]);
            monitor.Run();

            int unlockCount = 0;
            monitor.PropertyChanged += (s, e) =>
            {
                if (
                    e.PropertyName == nameof(TiltMonitor.IsTradingLocked)
                    && !monitor.IsTradingLocked
                )
                    unlockCount++;
            };

            monitor.OnTradeAdded(MakeWin(_now, 1));

            Assert.Equal(0, unlockCount);
        }

        [Fact]
        public void DecayTimer_AutomaticallyUpdatesScore_WhenTimePasses()
        {
            // Start with a 100 point loss
            var trade = MakeLoss(_now, 100);
            var monitor = CreateTiltMonitor([trade]);

            // Run evaluates the initial score (100) and starts the background timer
            monitor.Run();
            Assert.Equal(100.0, monitor.TiltScore, precision: 6);

            // Fast-forward the CustomTimeProvider by exactly 1 Half-Life (30 mins).
            // This natively triggers the background timer callback.
            _timeProvider.Advance(_now.AddMinutes(HalfLifeMinutes));

            // The timer should have successfully recalculated the score to exactly 50%
            Assert.Equal(50.0, monitor.TiltScore, precision: 6);

            // Fast-forward by another Half-Life
            _timeProvider.Advance(_now.AddMinutes(HalfLifeMinutes * 2));

            // Score should now be 25% of the original
            Assert.Equal(25.0, monitor.TiltScore, precision: 6);
        }

        [Fact]
        public void DecayTimer_FiresUnlockEvent_WhenScoreDropsBelowThreshold()
        {
            var bigLoss = MakeLoss(_now, LockThreshold + 500);
            var monitor = CreateTiltMonitor([bigLoss]);

            bool unlocked = false;
            monitor.PropertyChanged += (s, e) =>
            {
                if (
                    e.PropertyName == nameof(TiltMonitor.IsTradingLocked)
                    && !monitor.IsTradingLocked
                )
                    unlocked = true;
            };

            monitor.Run();

            Assert.True(monitor.TiltScore >= LockThreshold);
            Assert.False(unlocked);

            _timeProvider.Advance(_now.AddMinutes(HalfLifeMinutes * 2));

            Assert.True(
                monitor.TiltScore < UnlockThreshold,
                "Score should have dropped below UnlockThreshold"
            );
            Assert.True(unlocked, "PropertyChanged event should have fired indicating unlock");
        }
    }
}
