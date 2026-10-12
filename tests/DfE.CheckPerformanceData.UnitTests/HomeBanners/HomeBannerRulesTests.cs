using DfE.CheckPerformanceData.Application.HomeBanners;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.HomeBanners;

// The one place banner dates meet the clock (#566). "now" is UK wall-clock time, passed in,
// so every boundary is pinned here without a TimeProvider.
public sealed class HomeBannerRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0);

    [Fact]
    public void Off_WhenSwitchIsOff_WhateverTheDates() =>
        Assert.Equal(HomeBannerStatus.Off, HomeBannerRules.StatusOf(false, Now.AddDays(-1), Now.AddDays(1), Now));

    [Fact]
    public void Live_WhenOn_AndNoDates() =>
        Assert.Equal(HomeBannerStatus.Live, HomeBannerRules.StatusOf(true, null, null, Now));

    [Fact]
    public void Live_WhenOn_AndNowIsBetweenBothDates() =>
        Assert.Equal(HomeBannerStatus.Live, HomeBannerRules.StatusOf(true, Now.AddHours(-1), Now.AddHours(1), Now));

    [Fact]
    public void ShowFrom_IsInclusive() =>
        Assert.Equal(HomeBannerStatus.Live, HomeBannerRules.StatusOf(true, Now, null, Now));

    [Fact]
    public void ShowUntil_IsExclusive() =>
        Assert.Equal(HomeBannerStatus.Expired, HomeBannerRules.StatusOf(true, null, Now, Now));

    [Fact]
    public void Scheduled_WhenShowFromIsInTheFuture() =>
        Assert.Equal(HomeBannerStatus.Scheduled, HomeBannerRules.StatusOf(true, Now.AddMinutes(1), null, Now));

    [Fact]
    public void Expired_WhenShowUntilIsInThePast() =>
        Assert.Equal(HomeBannerStatus.Expired, HomeBannerRules.StatusOf(true, null, Now.AddMinutes(-1), Now));

    [Fact]
    public void Live_WhenOnlyShowFromIsSet_AndItHasPassed() =>
        Assert.Equal(HomeBannerStatus.Live, HomeBannerRules.StatusOf(true, Now.AddDays(-1), null, Now));

    [Fact]
    public void Live_WhenOnlyShowUntilIsSet_AndItIsAhead() =>
        Assert.Equal(HomeBannerStatus.Live, HomeBannerRules.StatusOf(true, null, Now.AddDays(1), Now));

    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(false, null, null, false)]
    [InlineData(true, "2026-10-09T12:00:00", null, true)]       // from == now → live
    [InlineData(true, null, "2026-10-09T12:00:00", false)]      // until == now → not live
    [InlineData(true, "2026-10-09T12:00:01", null, false)]
    [InlineData(true, null, "2026-10-09T11:59:59", false)]
    public void IsLive_MatchesStatus(bool enabled, string? from, string? until, bool expected)
    {
        DateTime? f = from is null ? null : DateTime.Parse(from);
        DateTime? u = until is null ? null : DateTime.Parse(until);
        Assert.Equal(expected, HomeBannerRules.IsLive(enabled, f, u, Now));
    }
}
