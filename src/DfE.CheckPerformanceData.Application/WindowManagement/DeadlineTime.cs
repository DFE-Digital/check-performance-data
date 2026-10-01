namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The clock time in a deadline sentence: "5pm" on the hour, "12:35pm" otherwise (AB#301022).
/// </summary>
/// <remarks>
/// Every deadline sentence used to print the hour alone. That was invisible while end dates sat on
/// the hour, and wrong as soon as one did not: an admin can type an end minute, and an exercise
/// closed early ends at whatever minute the admin pressed Close. "The deadline passed at 12pm"
/// for a 12:35 close misinforms a school about when it lost the ability to act.
///
/// Exercise dates are wall-clock values, so the value is formatted as it stands. Lives in
/// Application because both the web pages and the notification emails print deadlines.
/// </remarks>
public static class DeadlineTime
{
    /// <summary>
    /// The custom format for the time alone, for a sentence that formats time and date in one
    /// call (and so keeps whatever letter case that call produces).
    /// </summary>
    public static string Pattern(DateTime wallClock) => wallClock.Minute == 0 ? "htt" : "h:mmtt";

    /// <summary>The time in GOV.UK style: lower-case am/pm, no space.</summary>
    public static string Format(DateTime wallClock) =>
        wallClock.ToString(Pattern(wallClock)).ToLowerInvariant();
}
