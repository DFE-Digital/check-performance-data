using DfE.CheckPerformanceData.Web.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.PageTree;

// Publish windows are typed and shown in UK time but stored in UTC. Fixed dates cover a GMT day,
// a BST day and both 2026 changeover days (29 March and 25 October), so the results do not depend
// on the host's zone or today's date.
public sealed class PublishWindowTimeTests
{
    [Theory]
    [InlineData("2026-01-15T09:00", 2026, 1, 15, 9, 0)]
    [InlineData("2026-07-15T09:00", 2026, 7, 15, 8, 0)]
    [InlineData("2026-03-29T02:00", 2026, 3, 29, 1, 0)]
    [InlineData("2026-10-25T01:30", 2026, 10, 25, 0, 30)]
    public void TryParse_TreatsTheTypedValueAsUkTime(string typed, int y, int mo, int d, int h, int mi)
    {
        Assert.True(PublishWindowTime.TryParse(typed, "Publish from", out var utc, out var error));

        Assert.Null(error);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_BlankValue_IsAnOpenWindow(string? typed)
    {
        Assert.True(PublishWindowTime.TryParse(typed, "Publish to", out var utc, out var error));

        Assert.Null(utc);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_TimeInTheSkippedHour_IsRejectedWithAnExplanation()
    {
        Assert.False(PublishWindowTime.TryParse("2026-03-29T01:30", "Publish from", out var utc, out var error));

        Assert.Null(utc);
        Assert.Equal(
            "Publish from must be a real UK time. On 29 March 2026 the clocks go forward from 1am to 2am, so 1:30am does not exist",
            error);
    }

    [Fact]
    public void TryParse_UnreadableValue_IsRejected()
    {
        Assert.False(PublishWindowTime.TryParse("not a date", "Publish to", out var utc, out var error));

        Assert.Null(utc);
        Assert.Equal("Publish to must be a real date and time", error);
    }

    [Theory]
    [InlineData(2026, 1, 15, 9, 0, "2026-01-15T09:00")]   // GMT
    [InlineData(2026, 7, 15, 8, 0, "2026-07-15T09:00")]   // BST
    [InlineData(2026, 3, 29, 1, 0, "2026-03-29T02:00")]   // first instant of BST
    [InlineData(2026, 10, 25, 0, 30, "2026-10-25T01:30")] // first 1:30am (BST)
    [InlineData(2026, 10, 25, 1, 30, "2026-10-25T01:30")] // second 1:30am (GMT)
    public void ToInputValue_ShowsTheStoredUtcInstantInUkTime(int y, int mo, int d, int h, int mi, string expected)
    {
        var stored = new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

        Assert.Equal(expected, PublishWindowTime.ToInputValue(stored));
    }

    [Fact]
    public void ToInputValue_NoValue_IsEmpty()
    {
        Assert.Equal(string.Empty, PublishWindowTime.ToInputValue(null));
    }

    [Fact]
    public void ToInputValue_RoundTripsThroughTryParse()
    {
        var stored = new DateTime(2026, 10, 1, 13, 45, 0, DateTimeKind.Utc);

        Assert.True(PublishWindowTime.TryParse(PublishWindowTime.ToInputValue(stored), "Publish from", out var utc, out _));

        Assert.Equal(stored, utc);
    }
}
