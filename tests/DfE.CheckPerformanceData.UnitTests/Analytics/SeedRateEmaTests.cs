using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

// Locks the exponential-moving-average smoothing applied to the persisted
// SearchAnalytics:SeedSecondsPerEvent value after each non-cancelled seed run. The stored
// value must refresh after each subsequent seeding session so ETAs stay accurate —
// with a base default of 0.1 s/event so first-run ETAs are
// conservative. EMA with alpha=0.3 gives adaptive smoothing so a single outlier run does
// not dominate but consistent shifts (machine change) converge in a few runs.
public sealed class SeedRateEmaTests
{
    // Formula: newStored = 0.7 * oldStored + 0.3 * measured
    // With stored=0.1 measured=0.05: 0.7 * 0.1 + 0.3 * 0.05 = 0.07 + 0.015 = 0.085
    [Fact]
    public void Blend_MovesMidwayTowardMeasured()
    {
        var result = SeedRateEma.Blend(stored: 0.1, measured: 0.05);
        Assert.Equal(0.085, result, precision: 6);
    }

    [Fact]
    public void Blend_LeavesValueUnchanged_WhenMeasuredMatches()
    {
        var result = SeedRateEma.Blend(stored: 0.1, measured: 0.1);
        Assert.Equal(0.1, result, precision: 6);
    }

    [Fact]
    public void Blend_ConvergesTowardMeasured_OverMultipleRuns()
    {
        // Simulate a machine-swap where the seeder is now consistently 0.02 s/event but
        // the stored value from the old machine was 0.10. Five consecutive blends should
        // land close to the new measured value.
        var stored = 0.10;
        for (var i = 0; i < 5; i++)
        {
            stored = SeedRateEma.Blend(stored, measured: 0.02);
        }
        Assert.True(stored < 0.045,
            $"Expected the EMA to converge below 0.045 after 5 blends toward 0.02; got {stored}.");
    }

    [Fact]
    public void Blend_ClampsToPositive_WhenMeasuredIsZeroOrNegative()
    {
        // Zero or negative "measured" would be nonsensical (would imply an instant seed
        // or a wall-clock rewind). Blend must clamp before applying so the stored value
        // never falls to a bad floor.
        Assert.True(SeedRateEma.Blend(stored: 0.10, measured: 0.0) >= 0.001);
        Assert.True(SeedRateEma.Blend(stored: 0.10, measured: -0.5) >= 0.001);
    }
}
