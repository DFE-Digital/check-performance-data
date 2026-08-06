namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// A clock the test drives by hand. Lets a test assert "the absolute session cap has
/// elapsed" without sleeping for it — a sleeping test measures the host's wall clock,
/// and a host clock that steps backwards (routine on virtualised hosts, which resync
/// periodically) silently eats the margin and fails the test for no product reason.
/// </summary>
public sealed class SteppableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
