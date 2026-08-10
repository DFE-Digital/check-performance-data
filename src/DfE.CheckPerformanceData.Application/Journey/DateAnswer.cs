namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class DateAnswer
{
    public int Day { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }

    // True only when the three parts form a real calendar date. Optional Date questions left
    // blank are stored as 0/0/0, and partially-filled dates keep the missing parts as 0 — both
    // must be treated as "no date" rather than fed to the DateTime constructor. The year must
    // be 4 digits: validation enforces this, but answers saved before that rule existed could
    // still carry a 2-digit year, which must render as empty rather than "0026".
    public bool IsCompleteDate =>
        Month is >= 1 and <= 12 &&
        Year is >= 1000 and <= 9999 &&
        Day >= 1 && Day <= DateTime.DaysInMonth(Year, Month);

    // Formats as "d MMMM yyyy" (e.g. "12 March 2025") for display, or an empty string when the
    // date is not a complete, valid answer — so summary/confirmation rendering never throws.
    public string ToDisplayString() =>
        IsCompleteDate ? $"{Day} {new DateTime(Year, Month, Day):MMMM yyyy}" : string.Empty;

    // Comparable form for date rules. Null rather than throwing for the incomplete and
    // out-of-range cases IsCompleteDate already covers, so callers can treat "no usable date"
    // and "no answer" identically.
    public DateOnly? ToDateOnly() =>
        IsCompleteDate ? new DateOnly(Year, Month, Day) : null;
}
