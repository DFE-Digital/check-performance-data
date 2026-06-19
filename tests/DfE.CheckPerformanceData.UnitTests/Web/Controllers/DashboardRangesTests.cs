using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Models.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The dashboard's window/bucket selection is resolved against a server-side allow-list:
// a recognised preset window paired with the bucket sizes that make sense for it. Anything
// unrecognised snaps to a safe default rather than erroring — the form is a GET and a stale
// or hand-edited query string must always render a dashboard.
public sealed class DashboardRangesTests
{
    [Theory]
    [InlineData("1h")]
    [InlineData("6h")]
    [InlineData("24h")]
    [InlineData("7d")]
    public void Resolve_ReturnsTheMatchingPreset(string range)
    {
        var option = DashboardRanges.Resolve(range);

        Assert.Equal(range, option.Value);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var option = DashboardRanges.Resolve("7D");

        Assert.Equal("7d", option.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fortnight")]
    [InlineData("25h")]
    public void Resolve_UnknownRange_FallsBackToTheDefault(string? range)
    {
        var option = DashboardRanges.Resolve(range);

        Assert.Equal(DashboardRanges.DefaultValue, option.Value);
        Assert.Equal(TimeSpan.FromHours(24), option.Window);
    }

    [Fact]
    public void ResolveGranularity_NullOrMissing_UsesTheRangeDefault()
    {
        var option = DashboardRanges.Resolve("24h");

        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, null));
        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, ""));
    }

    [Fact]
    public void ResolveGranularity_AcceptsAPairedValue_CaseInsensitively()
    {
        var option = DashboardRanges.Resolve("24h");

        Assert.Equal(
            ThroughputGranularity.TenMinute,
            DashboardRanges.ResolveGranularity(option, "tenminute"));
    }

    [Fact]
    public void ResolveGranularity_UnpairedValue_SnapsToTheRangeDefault()
    {
        // Day buckets over a one-hour window would render a single meaningless bar.
        var option = DashboardRanges.Resolve("1h");

        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, "Day"));
    }

    [Fact]
    public void ResolveGranularity_NumericEnumSmuggling_SnapsToTheRangeDefault()
    {
        // Enum.TryParse accepts any integer string, so "999" parses to an undefined value;
        // the allow-list must reject it before it reaches the bucket-spec mapping.
        var option = DashboardRanges.Resolve("24h");

        Assert.Equal(option.DefaultGranularity, DashboardRanges.ResolveGranularity(option, "999"));
    }

    [Fact]
    public void EveryRange_PairsItsOwnDefaultGranularity()
    {
        Assert.All(DashboardRanges.All, option =>
            Assert.Contains(option.DefaultGranularity, option.AllowedGranularities));
    }

    [Fact]
    public void EveryGranularityLabel_IsHumanReadable()
    {
        Assert.All(DashboardRanges.All, option =>
            Assert.All(option.AllowedGranularities, g =>
                Assert.False(string.IsNullOrWhiteSpace(DashboardRanges.Describe(g)))));
    }
}
