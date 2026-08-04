namespace DfE.CheckPerformanceData.UnitTests.Analytics;

// Pure-logic contract for the "abandoned session" arithmetic that the recovery-stats
// reader applies over its three raw counts (total zero-result sessions, recovered
// sessions, feedback-sending sessions). The reader can't compute recovered∩feedback
// server-side without an extra aggregate — so it takes a conservative upper bound on
// "did not abandon" = min(total, recovered + feedback), and abandoned = total minus
// that. Result: when a session BOTH recovered AND sent feedback, we slightly UNDERstate
// abandoned rather than double-count it. The drill-in surfaces the precise per-session
// state so the headline stays simple and never over-reads as alarming.
//
// This lives as a unit test even though the formula is inlined in the DB reader —
// pinning the math contract in a fast test means a refactor that extracts the formula
// into a helper won't drift the meaning.
public sealed class ZeroResultRecoveryMathTests
{
    // Formula-under-test — a verbatim reproduction of the two lines inside
    // SearchAnalyticsQueryService.GetZeroResultRecoveryStatsAsync (near L1298-L1299).
    // If either helper below changes shape, the reader implementation must be updated in
    // lockstep — the assertion set on the reader (see SearchAnalyticsRecoveryAndJourneyTests)
    // will catch a drift end-to-end.
    private static int Abandoned(int total, int recovered, int feedback)
    {
        var didNotAbandon = Math.Min(total, recovered + feedback);
        return Math.Max(0, total - didNotAbandon);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]         // Nothing in the window → nothing abandoned.
    [InlineData(3, 3, 0, 0)]         // Everyone recovered → nothing abandoned.
    [InlineData(3, 0, 3, 0)]         // Everyone sent feedback → nothing abandoned.
    [InlineData(3, 0, 0, 3)]         // Nobody did either → all three abandoned.
    [InlineData(5, 2, 1, 2)]         // Disjoint: 2 recovered + 1 feedback + 2 silent.
    [InlineData(10, 8, 3, 0)]        // Cap on the overlap: recovered + feedback > total → clamp to 0 abandoned.
    [InlineData(4, 2, 2, 0)]         // Exact sum equals total → 0 abandoned (may under-state if overlap exists).
    public void Abandoned_MatchesReaderFormula(
        int total, int recovered, int feedback, int expectedAbandoned)
    {
        Assert.Equal(expectedAbandoned, Abandoned(total, recovered, feedback));
    }

    [Fact]
    public void Abandoned_IsNeverNegative_EvenWhenSumExceedsTotal()
    {
        // The Math.Max(0, ...) guard is here because recovered + feedback can exceed
        // total when a session both recovered AND sent feedback (double-counted). A
        // negative "abandoned" would render as a broken tile in the dashboard.
        Assert.Equal(0, Abandoned(total: 5, recovered: 5, feedback: 5));
        Assert.Equal(0, Abandoned(total: 1, recovered: 1, feedback: 1));
    }

    [Fact]
    public void Abandoned_IsNeverGreaterThanTotal()
    {
        // Upper bound: abandoned ≤ total, always. A tile that shows "6 abandoned of 5
        // sessions" would break the "%" formatter downstream (>100%).
        for (var total = 0; total < 20; total++)
        for (var rec = 0; rec < 25; rec++)
        for (var fb = 0; fb < 25; fb++)
        {
            var a = Abandoned(total, rec, fb);
            Assert.InRange(a, 0, total);
        }
    }
}
