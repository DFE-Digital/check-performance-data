using System.Globalization;

namespace DfE.CheckPerformanceData.E2ETests.Retrying;

/// <summary>
/// Run-level retry policy for the suite's custom <see cref="RetryFactAttribute"/> and
/// <see cref="RetryTheoryAttribute"/>.
///
/// The attempt count is one setting for the whole suite, not a literal on each
/// attribute, so the failure budget is a single knob instead of ~90 hard-coded
/// arguments. Retrying is off by default: a skipped-over flake costs CI a long
/// poke-in-the-dark, and re-running non-idempotent journeys (submit enquiry,
/// conflict checks) moves the system under test and hides the original error.
///
/// Set <c>CPD_E2E_RETRY_ATTEMPTS</c> to raise it (CI can override per run).
/// </summary>
public static class RetrySettings
{
    public const int DefaultMaxRetries = 1;

    /// <summary>Total attempts (including the first) allowed per test. 1 disables retrying.</summary>
    public static int MaxRetries { get; } = ReadMaxRetries();

    private static int ReadMaxRetries()
    {
        var raw = Environment.GetEnvironmentVariable("CPD_E2E_RETRY_ATTEMPTS");
        if (!string.IsNullOrWhiteSpace(raw) &&
            int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 1)
        {
            return parsed;
        }

        return DefaultMaxRetries;
    }
}