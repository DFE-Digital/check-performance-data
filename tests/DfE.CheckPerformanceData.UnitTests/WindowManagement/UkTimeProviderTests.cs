using System.Globalization;
using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #535: checking-exercise dates are UK wall-clock values, so the clock they are compared with
// must be a UK clock whatever zone the host runs in. The containers run on UTC.
public sealed class UkTimeProviderTests
{
    [Fact]
    public void Local_time_is_an_hour_ahead_of_UTC_during_British_Summer_Time()
    {
        DateTimeOffset local = new UkClockAt("2026-07-15T12:33:00Z").GetLocalNow();

        Assert.Equal(new DateTime(2026, 7, 15, 13, 33, 0), local.DateTime);
        Assert.Equal(TimeSpan.FromHours(1), local.Offset);
    }

    [Fact]
    public void Local_time_equals_UTC_in_winter()
    {
        DateTimeOffset local = new UkClockAt("2027-01-15T12:33:00Z").GetLocalNow();

        Assert.Equal(new DateTime(2027, 1, 15, 12, 33, 0), local.DateTime);
        Assert.Equal(TimeSpan.Zero, local.Offset);
    }

    [Theory]
    // The clocks go back on 25 October 2026 at 01:00 UTC: 01:59:59 BST is followed by 01:00:00 GMT.
    [InlineData("2026-10-25T00:59:59Z", "2026-10-25T01:59:59")]
    [InlineData("2026-10-25T01:00:00Z", "2026-10-25T01:00:00")]
    // The clocks go forward on 28 March 2027 at 01:00 UTC: 00:59:59 GMT is followed by 02:00:00 BST.
    [InlineData("2027-03-28T00:59:59Z", "2027-03-28T00:59:59")]
    [InlineData("2027-03-28T01:00:00Z", "2027-03-28T02:00:00")]
    public void Local_time_follows_the_UK_clock_change(string utcNow, string expectedLocal)
    {
        DateTime expected = DateTime.Parse(expectedLocal, CultureInfo.InvariantCulture);

        Assert.Equal(expected, new UkClockAt(utcNow).GetLocalNow().DateTime);
    }

    [Fact]
    public void The_shared_instance_is_the_system_clock_with_the_UK_zone()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset now = UkTimeProvider.Instance.GetUtcNow();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.InRange(now, before, after);
        Assert.Same(UkTimeProvider.Zone, UkTimeProvider.Instance.LocalTimeZone);
        Assert.Equal(TimeSpan.FromHours(1), UkTimeProvider.Zone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.Zero, UkTimeProvider.Zone.GetUtcOffset(new DateTime(2027, 1, 15, 12, 0, 0, DateTimeKind.Utc)));
    }
}
