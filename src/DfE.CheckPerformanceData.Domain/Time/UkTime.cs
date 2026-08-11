namespace DfE.CheckPerformanceData.Domain.Time;

/// <summary>
/// UK wall-clock time. Checking window boundaries (<c>StartDate</c>/<c>EndDate</c>) are wall-clock
/// UK deadlines chosen by an admin — "closes 17:00" means 17:00 in London, not 17:00 UTC — so any
/// open/closed comparison has to be made against UK local time, not against a UTC instant.
///
/// Deliberately does NOT use <see cref="TimeProvider.GetLocalNow"/>: that resolves to
/// <see cref="TimeZoneInfo.Local"/>, which is the host's zone. The deploy containers run UTC, so
/// GetLocalNow() is an hour out through BST while looking correct on a developer machine set to
/// London.
/// </summary>
public static class UkTime
{
    /// <summary>
    /// The Europe/London zone — BST in summer, GMT in winter. The IANA id resolves on the Linux
    /// deploy target and, via .NET's ICU mapping, on Windows too. Resolved once at type load.
    /// </summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    /// <summary>UK wall-clock now, taken from the supplied clock's UTC instant.</summary>
    public static DateTime Now(TimeProvider clock) =>
        TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, Zone);
}
