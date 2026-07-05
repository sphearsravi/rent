using RentCollection.Core.Leases;
using Xunit;

namespace RentCollection.Tests.Leases;

public class LeaseMathTests
{
    [Theory]
    [InlineData(1, 2026, 6, 2026, 6, 1)]    // normal day
    [InlineData(15, 2026, 6, 2026, 6, 15)]  // normal day
    [InlineData(31, 2026, 6, 2026, 6, 30)]  // 30-day month clamps
    [InlineData(31, 2026, 2, 2026, 2, 28)]  // Feb non-leap clamps
    [InlineData(31, 2024, 2, 2024, 2, 29)]  // Feb leap clamps
    [InlineData(31, 2026, 4, 2026, 4, 30)]  // Apr clamps
    [InlineData(0, 2026, 6, 2026, 6, 1)]    // 0 or below → day 1
    [InlineData(-5, 2026, 6, 2026, 6, 1)]   // negative → day 1
    public void ComputeDueDate_clamps_to_month_length(
        int paymentDueDay, int year, int month,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var actual = LeaseMath.ComputeDueDate(paymentDueDay, year, month);
        Assert.Equal(new DateTime(expectedYear, expectedMonth, expectedDay), actual);
    }

    [Fact]
    public void DaysRemaining_yesterday_is_minus_one()
    {
        var yesterday = DateTime.Today.AddDays(-1);
        Assert.Equal(-1, LeaseMath.DaysRemaining(yesterday));
    }

    [Fact]
    public void DaysRemaining_today_is_zero()
    {
        Assert.Equal(0, LeaseMath.DaysRemaining(DateTime.Today));
    }

    [Fact]
    public void DaysRemaining_tomorrow_is_one()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        Assert.Equal(1, LeaseMath.DaysRemaining(tomorrow));
    }

    [Fact]
    public void DaysRemaining_ignores_time_component()
    {
        // A lease end-date stored with a time of 23:59:59 still resolves to
        // the same day as one stored at 00:00:00 — the bug fixed by Flaw #14.
        var endA = DateTime.Today.AddDays(5);
        var endB = endA.AddHours(23).AddMinutes(59).AddSeconds(59);
        Assert.Equal(LeaseMath.DaysRemaining(endA), LeaseMath.DaysRemaining(endB));
    }

    [Fact]
    public void IsPastEnd_yesterday_true_today_false_tomorrow_false()
    {
        Assert.True(LeaseMath.IsPastEnd(DateTime.Today.AddDays(-1)));
        Assert.False(LeaseMath.IsPastEnd(DateTime.Today));
        Assert.False(LeaseMath.IsPastEnd(DateTime.Today.AddDays(1)));
    }
}
