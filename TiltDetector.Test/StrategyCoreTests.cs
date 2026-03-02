using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace TiltDetector.Test
{
    public class StrategyCoreTests
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

        public StrategyCoreTests()
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

        private StrategyCore CreateStrategyCore(IEnumerable<Trade> trades)
        {
            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns(trades);
            return new StrategyCore(_contextMock.Object);
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
            var core = CreateStrategyCore([]);
            core.Run();

            Assert.Equal(0, core.TiltScore);
        }

        [Fact]
        public void TiltScore_IsZero_WhenOnlyWinningTrades()
        {
            var core = CreateStrategyCore([MakeWin(_now.AddMinutes(-5), 100)]);
            core.Run();

            Assert.Equal(0, core.TiltScore);
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
            var core = CreateStrategyCore([MakeLoss(tradeTime, 100)]);

            core.Run();
            Assert.Equal(expectedScore, core.TiltScore, precision: 6);
        }

        [Fact]
        public void TiltScore_SumsMultipleLosses()
        {
            var trades = new[] { MakeLoss(_now, 100), MakeLoss(_now, 50) };
            var core = CreateStrategyCore(trades);
            core.Run();

            Assert.Equal(150.0, core.TiltScore, precision: 6);
        }

        [Fact]
        public void TiltScore_IgnoresTrades_FromOtherAccounts()
        {
            var core = CreateStrategyCore([MakeLossOnOtherAccount(_now, 500)]);
            core.Run();

            Assert.Equal(0, core.TiltScore);
        }

        [Fact]
        public void TiltScore_UpdatesOnTradeAdded()
        {
            var core = CreateStrategyCore([]);
            core.Run();
            Assert.Equal(0, core.TiltScore);

            var newTrade = MakeLoss(_now, 100);
            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns([newTrade]);

            core.OnTradeAdded(newTrade);

            Assert.Equal(100.0, core.TiltScore, precision: 6);
        }

        [Fact]
        public void TradingLocked_Fires_WhenScoreCrossesLockThreshold()
        {
            var core = CreateStrategyCore([]);
            core.Run();

            bool locked = false;
            core.TradingLocked += () => locked = true;

            var bigLoss = MakeLoss(_now, LockThreshold + 1);
            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns([bigLoss]);

            core.OnTradeAdded(bigLoss);

            Assert.True(locked);
        }

        [Fact]
        public void TradingLocked_DoesNotFire_WhenAlreadyLocked()
        {
            var bigLoss = MakeLoss(_now, LockThreshold + 1);
            var core = CreateStrategyCore([bigLoss]);
            core.Run();

            int lockCount = 0;
            core.TradingLocked += () => lockCount++;

            core.OnTradeAdded(bigLoss);

            Assert.Equal(0, lockCount);
        }

        [Fact]
        public void TradingUnlocked_Fires_WhenScoreDropsBelowUnlockThreshold()
        {
            var bigLoss = MakeLoss(_now, LockThreshold + 1);
            var core = CreateStrategyCore([bigLoss]);
            core.Run();

            bool unlocked = false;
            core.TradingUnlocked += () => unlocked = true;

            _contextMock
                .Setup(c => c.GetTrades(It.IsAny<TradesHistoryRequestParameters>()))
                .Returns([]);

            core.OnTradeAdded(MakeWin(_now, 1));

            Assert.True(unlocked);
        }

        [Fact]
        public void TradingUnlocked_DoesNotFire_WhenNotLocked()
        {
            var core = CreateStrategyCore([]);
            core.Run();

            int unlockCount = 0;
            core.TradingUnlocked += () => unlockCount++;

            core.OnTradeAdded(MakeWin(_now, 1));

            Assert.Equal(0, unlockCount);
        }

        [Fact]
        public void DecayTimer_AutomaticallyUpdatesScore_WhenTimePasses()
        {
            // Start with a 100 point loss
            var trade = MakeLoss(_now, 100);
            var core = CreateStrategyCore([trade]);

            // Run evaluates the initial score (100) and starts the background timer
            core.Run();
            Assert.Equal(100.0, core.TiltScore, precision: 6);

            // Fast-forward the CustomTimeProvider by exactly 1 Half-Life (30 mins).
            // This natively triggers the background timer callback.
            _timeProvider.Advance(_now.AddMinutes(HalfLifeMinutes));

            // The timer should have successfully recalculated the score to exactly 50%
            Assert.Equal(50.0, core.TiltScore, precision: 6);

            // Fast-forward by another Half-Life
            _timeProvider.Advance(_now.AddMinutes(HalfLifeMinutes * 2));

            // Score should now be 25% of the original
            Assert.Equal(25.0, core.TiltScore, precision: 6);
        }

        [Fact]
        public void DecayTimer_FiresUnlockEvent_WhenScoreDropsBelowThreshold()
        {
            // Start with a massive loss (e.g., 2500) that immediately locks the strategy
            var bigLoss = MakeLoss(_now, LockThreshold + 500);
            var core = CreateStrategyCore([bigLoss]);

            bool unlocked = false;
            core.TradingUnlocked += () => unlocked = true;

            core.Run();

            // Verify we are currently locked
            Assert.True(core.TiltScore >= LockThreshold);
            Assert.False(unlocked);

            // Fast-forward time by 2 Half-Lives (60 minutes).
            // The score drops to 25% of the original (2500 * 0.25 = 625),
            // which is well below the UnlockThreshold (1500).
            _timeProvider.Advance(_now.AddMinutes(HalfLifeMinutes * 2));

            // The timer executed, the score decayed, and the unlock event fired!
            Assert.True(
                core.TiltScore < UnlockThreshold,
                "Score should have dropped below UnlockThreshold"
            );
            Assert.True(unlocked, "TradingUnlocked event should have fired");
        }
    }
}
