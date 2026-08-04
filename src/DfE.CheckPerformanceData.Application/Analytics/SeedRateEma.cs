namespace DfE.CheckPerformanceData.Application.Analytics;

// Adaptive smoothing of the persisted per-event seconds rate written by the seed-sample-
// search-data page. After each non-cancelled seed the controller measures actual seconds
// per event, then blends into the stored value via a single-pole exponential moving
// average with alpha=0.3 (i.e. old value carries 70% weight, new value 30%). This gives:
//   * A single outlier run (a slow first run on a cold container, a burst of GC pauses)
//     doesn't dominate the estimate.
//   * A genuine machine-throughput shift (running on a laptop vs a workstation) converges
//     in three-to-five subsequent runs.
// The stored value is refreshed after every completed seeding session so the ETA shown
// on the seed page stays reasonably accurate over time.
public static class SeedRateEma
{
    // Weight given to the most recent measurement. 0.3 is the pragmatic choice — a
    // half-life around two runs, which matches how a developer would rely on the number:
    // if you kick off two consecutive seeds on a slower machine, the ETA on the second
    // will already reflect the new reality; a single hiccup is quickly forgotten.
    private const double Alpha = 0.3;

    // Absolute floor to keep the stored value strictly positive. A zero or negative
    // "measured" would imply an instant seed or a wall-clock rewind and would eventually
    // drive the stored rate to zero (division-by-zero on the client-side ETA math).
    private const double FloorSeconds = 0.001;

    public static double Blend(double stored, double measured)
    {
        var safeMeasured = measured > 0 ? measured : FloorSeconds;
        var safeStored = stored > 0 ? stored : FloorSeconds;
        var result = (1.0 - Alpha) * safeStored + Alpha * safeMeasured;
        return result < FloorSeconds ? FloorSeconds : result;
    }
}
