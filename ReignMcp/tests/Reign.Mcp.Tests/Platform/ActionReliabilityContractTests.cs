using ReignBeta.PartyAgency;
using Xunit;

namespace Reign.Mcp.Tests.Platform;

public sealed class ActionReliabilityContractTests
{
    [Theory]
    [InlineData(91081, 0, 0)]
    [InlineData(91112.499, 0, 0)]
    [InlineData(91112.5, 1, 500)]
    [InlineData(91144, 2, 1000)]
    [InlineData(91175.5, 3, 1500)]
    public void CapturedSeasonalRateAccruesOnlyCompletedReignPeriods(double now, int periods, long gold)
    {
        Assert.True(ReignGuestWagePolicy.TryDue(500, 31.5, 91112.5, now, 0, out int actualPeriods, out long actualGold));
        Assert.Equal(periods, actualPeriods);
        Assert.Equal(gold, actualGold);
    }

    [Fact]
    public void SettledPeriodDoesNotPayTwiceAndDepartureStopsAccrual()
    {
        Assert.True(ReignGuestWagePolicy.TryDue(500, 21, 91102, 91102, 0, out int paidPeriods, out long first));
        Assert.Equal(500, first);
        double next = 91102 + paidPeriods * 21;
        Assert.True(ReignGuestWagePolicy.TryDue(500, 21, next, 91102, 0, out _, out long duplicate));
        Assert.Equal(0, duplicate);
        Assert.True(ReignGuestWagePolicy.TryDue(500, 21, 91102, 92000, 91103, out _, out long overdue));
        Assert.Equal(500, overdue);
        Assert.True(ReignGuestWagePolicy.TryDue(500, 21, next, 92000, 91103, out _, out long settledDebt));
        Assert.Equal(0, settledDebt);
    }

    [Fact]
    public void InterruptedHourlyTicksEqualContinuousAccrual()
    {
        double next = 31.5;
        long serial = 0;
        for (int hour = 1; hour <= 126 * 24; hour++)
        {
            Assert.True(ReignGuestWagePolicy.TryDue(500, 31.5, next, hour / 24d, 0, out int periods, out long gold));
            serial += gold;
            next += periods * 31.5;
        }
        Assert.True(ReignGuestWagePolicy.TryDue(500, 31.5, 31.5, 126, 0, out _, out long catchup));
        Assert.Equal(2000, serial);
        Assert.Equal(serial, catchup);
    }

    [Fact]
    public void InvalidSchedulesAndOverflowCannotCreateMoney()
    {
        foreach (double period in new[] { 0, -1, double.NaN, double.PositiveInfinity })
            Assert.False(ReignGuestWagePolicy.TryDue(500, period, 21, 42, 0, out _, out _));
        Assert.False(ReignGuestWagePolicy.TryDue(500, 1, 1, double.MaxValue, 0, out _, out _));
        Assert.True(ReignGuestWagePolicy.TryDue(int.MaxValue, 1, 1, 2, 0, out _, out long due));
        Assert.Equal(2L * int.MaxValue, due); // Native payer validation rejects this without integer wraparound.
    }
}
