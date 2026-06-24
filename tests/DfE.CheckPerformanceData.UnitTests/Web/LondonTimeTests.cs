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
}
