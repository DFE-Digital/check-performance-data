using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

// Log-scale companion for the linear tick helpers in ChartAxisTicks. Same partials
// consume both — a chart with a heavy latency outlier squishing the mass at the
// bottom of the plot flips to a base-10 Y axis so the sub-100ms cluster stays
// readable. Trigger rule: max(y) / max(min(y), 1) > 20. When triggered the caller
// asks for log fractions (powers of 10 by default; half-decade if the range is
// under three decades) and log labels that stay in normal units (1 ms / 10 ms /
// 100 ms / 1000 ms) so the reader still recognises the values.
public sealed class ChartAxisTicksLogScaleTests
{
    // --- ShouldUseLogScale --------------------------------------------------

    [Theory]
    [InlineData(1, 21)]        // just over the 20× ratio
    [InlineData(10, 250)]      // classic squish: 10ms floor, 250ms peak
    [InlineData(5, 3000)]      // 3 second outlier over sub-100ms mass
    [InlineData(0, 100)]       // min 0 is clamped to 1 → 100 / 1 = 100
    public void ShouldUseLogScale_ReturnsTrue_WhenMaxOverClampedMinExceeds20(int min, int max)
    {
        Assert.True(ChartAxisTicks.ShouldUseLogScale(min, max));
    }

    [Theory]
    [InlineData(1, 20)]        // exactly at the boundary — not triggered
    [InlineData(10, 200)]      // exactly 20× → not triggered
    [InlineData(50, 900)]      // <20× → linear stays legible
    [InlineData(100, 150)]     // tight cluster
    public void ShouldUseLogScale_ReturnsFalse_WhenMaxOverClampedMinIsAtOrBelow20(int min, int max)
    {
        Assert.False(ChartAxisTicks.ShouldUseLogScale(min, max));
    }

    [Fact]
    public void ShouldUseLogScale_ReturnsFalse_ForEmptyOrDegenerate()
    {
        // Both zero, negative max, and max < min are all "no data / bad data" — the
        // linear renderer handles those with its zero-safe fallback.
        Assert.False(ChartAxisTicks.ShouldUseLogScale(0, 0));
        Assert.False(ChartAxisTicks.ShouldUseLogScale(0, -1));
        Assert.False(ChartAxisTicks.ShouldUseLogScale(100, 50));
    }

    // --- YAxisFractionsLog --------------------------------------------------

    [Fact]
    public void YAxisFractionsLog_ProducesPowerOfTenTicks_WhenRangeExceedsThreeDecades()
    {
        // 1 ms .. 3000 ms → spans ~3.5 decades. Full decades = 1, 10, 100, 1000, [3000-topcap]
        var ticks = ChartAxisTicks.YAxisFractionsLog(minValue: 1, maxValue: 3000);

        Assert.NotEmpty(ticks);
        // First tick must be at fraction 0 (the axis floor).
        Assert.Equal(0d, ticks[0], precision: 4);
        // Last tick must sit at fraction 1 (the axis ceiling / max).
        Assert.Equal(1d, ticks[^1], precision: 4);
        // Every fraction is between 0 and 1 inclusive.
        Assert.All(ticks, f => Assert.InRange(f, 0d, 1d));

        // Assert the interior ticks are power-of-10 values. Convert each fraction back
        // to its ms value by inverting the log-scale mapping the partial uses.
        var logMin = Math.Log10(1);   // 0
        var logMax = Math.Log10(3000); // ~3.477
        var interiorValues = ticks
            .Skip(1).SkipLast(1)
            .Select(f => Math.Pow(10, logMin + f * (logMax - logMin)))
            .ToList();
        Assert.Contains(interiorValues, v => Math.Abs(v - 10) < 0.01);
        Assert.Contains(interiorValues, v => Math.Abs(v - 100) < 0.01);
        Assert.Contains(interiorValues, v => Math.Abs(v - 1000) < 0.01);
    }

    [Fact]
    public void YAxisFractionsLog_ProducesHalfDecadeTicks_WhenRangeUnderThreeDecades()
    {
        // 1 ms .. 300 ms → 2.5 decades. Half-decade ticks: 1, 3, 10, 30, 100, 300.
        var ticks = ChartAxisTicks.YAxisFractionsLog(minValue: 1, maxValue: 300);

        Assert.NotEmpty(ticks);
        Assert.Equal(0d, ticks[0], precision: 4);
        Assert.Equal(1d, ticks[^1], precision: 4);

        var logMin = Math.Log10(1);
        var logMax = Math.Log10(300);
        var values = ticks
            .Select(f => Math.Pow(10, logMin + f * (logMax - logMin)))
            .ToList();
        // Half-decade fingerprint: interior should include a "3-multiple" value
        // (~3 ms and/or ~30 ms) that pure power-of-10 ticks would not have.
        Assert.Contains(values, v => Math.Abs(v - 3) < 0.1 || Math.Abs(v - 30) < 0.5);
    }

    [Fact]
    public void YAxisFractionsLog_ClampsMinValueToOne_WhenMinIsZeroOrNegative()
    {
        // log(0) blows up. The helper must clamp to 1 (matching the same clamp the
        // partial applies when it maps a y=0 point).
        var ticks = ChartAxisTicks.YAxisFractionsLog(minValue: 0, maxValue: 100);

        Assert.NotEmpty(ticks);
        Assert.Equal(0d, ticks[0], precision: 4);
        Assert.Equal(1d, ticks[^1], precision: 4);
    }

    // --- YAxisLabelLog ------------------------------------------------------

    [Fact]
    public void YAxisLabelLog_FormatsValueInNormalUnits_WithSuffix()
    {
        // Fraction 0 at min=1, fraction 1 at max=1000 → label should read "1 ms" / "1,000 ms".
        Assert.Equal("1 ms",     ChartAxisTicks.YAxisLabelLog(fraction: 0d, minValue: 1, maxValue: 1000, " ms"));
        Assert.Equal("1,000 ms", ChartAxisTicks.YAxisLabelLog(fraction: 1d, minValue: 1, maxValue: 1000, " ms"));
        // Mid-fraction (log space) → 100 ms.
        Assert.Equal("100 ms", ChartAxisTicks.YAxisLabelLog(fraction: 2d / 3d, minValue: 1, maxValue: 1000, " ms"));
    }
}
