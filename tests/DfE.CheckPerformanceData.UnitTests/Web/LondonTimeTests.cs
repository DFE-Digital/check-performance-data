using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class LondonTimeTests
{
    [Fact]
    public void ToLondon_AddsOneHour_ForUtcInstantDuringBst()
    {
        // 24 June is British Summer Time (UTC+1).
        var utc = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);

        var london = LondonTime.ToLondon(utc);

        Assert.Equal(new DateTime(2026, 6, 24, 13, 0, 0), london);
    }

    [Fact]
    public void ToLondon_KeepsSameTime_ForUtcInstantDuringGmt()
    {
        // 15 January is Greenwich Mean Time (UTC+0).
        var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var london = LondonTime.ToLondon(utc);

        Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0), london);
    }

    [Fact]
    public void ToLondon_TreatsUnspecifiedKindAsUtc()
    {
        var unspecified = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Unspecified);
        var utc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc);

        Assert.Equal(LondonTime.ToLondon(utc), LondonTime.ToLondon(unspecified));
    }

    [Fact]
    public void ToLondon_ConvertsLocalKind_ViaUtc()
    {
        var utc = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime(); // Kind = Local

        Assert.Equal(LondonTime.ToLondon(utc), LondonTime.ToLondon(local));
    }

    [Fact]
    public void ToLondonString_FormatsConvertedValue()
    {
        var utc = new DateTime(2026, 6, 24, 12, 5, 0, DateTimeKind.Utc);

        Assert.Equal("24 Jun 2026 13:05", utc.ToLondonString(LondonTime.Friendly));
    }

    [Fact]
    public void ToLondonString_ReturnsEmpty_ForNull()
    {
        DateTime? value = null;

        Assert.Equal(string.Empty, value.ToLondonString(LondonTime.Friendly));
    }

    [Fact]
    public void LondonToUtc_SubtractsOneHour_ForBstWallClock()
    {
        var wallClock = new DateTime(2026, 6, 24, 13, 0, 0, DateTimeKind.Unspecified);

        var utc = LondonTime.LondonToUtc(wallClock);

        Assert.Equal(new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void LondonToUtc_RoundTripsWithToLondon()
    {
        var wallClock = new DateTime(2026, 6, 24, 13, 0, 0, DateTimeKind.Unspecified);

        var roundTripped = LondonTime.ToLondon(LondonTime.LondonToUtc(wallClock));

        Assert.Equal(wallClock, roundTripped);
    }

    // A typed UK wall-clock time is resolved to a single UTC instant. Fixed dates cover a GMT day,
    // a BST day and both 2026 changeover days (clocks go forward at 1am GMT on 29 March and back
    // at 2am BST on 25 October), so the results do not depend on the host's zone or today's date.
    [Theory]
    [InlineData("2026-01-15T09:00", "2026-01-15T09:00")] // GMT: UK time equals UTC
    [InlineData("2026-07-15T09:00", "2026-07-15T08:00")] // BST: UK time is UTC+1
    [InlineData("2026-03-29T00:59", "2026-03-29T00:59")] // last minute of GMT before the skip
    [InlineData("2026-03-29T02:00", "2026-03-29T01:00")] // first minute of BST after the skip
    [InlineData("2026-10-25T00:59", "2026-10-24T23:59")] // BST, before the repeated hour
    [InlineData("2026-10-25T01:00", "2026-10-25T00:00")] // repeated hour: first occurrence (BST)
    [InlineData("2026-10-25T01:30", "2026-10-25T00:30")] // repeated hour: first occurrence (BST)
    [InlineData("2026-10-25T02:00", "2026-10-25T02:00")] // GMT again after the repeated hour
    public void TryLondonToUtc_ResolvesWallClockToUtc(string wallClock, string expectedUtc)
    {
        var typed = DateTime.Parse(wallClock, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(LondonTime.TryLondonToUtc(typed, out var utc));

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(DateTime.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture), utc);
    }

    [Theory]
    [InlineData("2026-03-29T01:00")]
    [InlineData("2026-03-29T01:30")]
    [InlineData("2026-03-29T01:59")]
    public void TryLondonToUtc_RejectsTimeInTheHourSkippedWhenClocksGoForward(string wallClock)
    {
        var typed = DateTime.Parse(wallClock, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(LondonTime.TryLondonToUtc(typed, out _));
    }

    [Fact]
    public void TryLondonToUtc_IgnoresTheKindOfTheTypedValue()
    {
        var typed = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc);

        Assert.True(LondonTime.TryLondonToUtc(typed, out var utc));

        Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc), utc);
    }
}
