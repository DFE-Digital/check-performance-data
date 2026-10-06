namespace DfE.CheckPerformanceData.Application.Common;

/// <summary>
/// The service's clock: the system clock, with "local" time fixed to UK time (Europe/London —
/// GMT in winter, BST in summer) whatever zone the host or container is set to.
/// </summary>
/// <remarks>
/// Checking-exercise and window dates are UK wall-clock values, typed by an admin and stored
/// without a zone, and every reader compares them with <see cref="TimeProvider.GetLocalNow"/>.
/// The containers run on UTC, so with the system provider every exercise opened and closed an
/// hour late during British Summer Time (#535). Registering this provider instead moves every
/// reader at once: none of them converts for itself, so they cannot disagree with each other,
/// and the end date the early close stamps is on the same clock it is later compared with.
///
/// Only the zone is overridden. The UTC instant, timestamps and timers are the system's, so
/// anything that takes this provider for those (sessions, caches, retry timers) is unaffected.
///
/// One consequence of comparing wall-clock values: on the night the clocks go back, 01:00 to
/// 02:00 happens twice. A date inside that hour is passed twice, an hour apart. The default
/// times (00:00 and 17:00) are nowhere near it.
/// </remarks>
public class UkTimeProvider : TimeProvider
{
    /// <summary>
    /// The IANA id resolves from tzdata on the Linux images and, through ICU, on Windows.
    /// </summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    /// <summary>The one instance both hosts register.</summary>
    public static UkTimeProvider Instance { get; } = new();

    /// <summary>Protected so a test can stop the clock at an instant and keep this zone.</summary>
    protected UkTimeProvider()
    {
    }

    public sealed override TimeZoneInfo LocalTimeZone => Zone;
}
