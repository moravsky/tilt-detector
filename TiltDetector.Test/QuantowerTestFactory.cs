using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using TradingPlatform.BusinessLayer;

namespace TiltDetector.Test
{
    public static class QuantowerTestFactory
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static Account CreateAccount(string id)
        {
            var account = (Account)RuntimeHelpers.GetUninitializedObject(typeof(Account));
            typeof(Account).GetProperty("Id", Flags)!.SetValue(account, id);
            return account;
        }

        public static Trade CreateTrade(DateTime utcTime, double grossPnl, Account account)
        {
            var trade = (Trade)RuntimeHelpers.GetUninitializedObject(typeof(Trade));
            typeof(Trade).GetProperty("DateTime", Flags)!.SetValue(trade, utcTime);
            typeof(Trade).GetProperty("Account", Flags)!.SetValue(trade, account);
            typeof(Trade)
                .GetProperty("GrossPnl", Flags)!
                .SetValue(trade, new PnLItem { Value = grossPnl });
            return trade;
        }
    }
}
