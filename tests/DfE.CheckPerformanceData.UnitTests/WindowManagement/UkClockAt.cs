using System.Globalization;
using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

/// <summary>
/// #535: the production UK clock, stopped at one UTC instant. It derives from
/// <see cref="UkTimeProvider"/> rather than re-declaring the zone, so a test that uses it is
/// reading the zone the site reads.
/// </summary>
public sealed class UkClockAt(DateTimeOffset utcNow) : UkTimeProvider
{
    /// <param name="utcIso">An ISO 8601 UTC instant, for example <c>2026-07-15T16:30:00Z</c>.</param>
    public UkClockAt(string utcIso)
        : this(DateTimeOffset.Parse(utcIso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
    {
    }

    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
