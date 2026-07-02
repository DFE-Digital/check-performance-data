namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class DateAnswer
{
    public int Day { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }

    // True only when the three parts form a real calendar date. Optional Date questions left
    // blank are stored as 0/0/0, and partially-filled dates keep the missing parts as 0 — both
    // must be treated as "no date" rather than fed to the DateTime constructor.
    public bool IsCompleteDate =>
        Month is >= 1 and <= 12 &&
        Year is >= 1 and <= 9999 &&
        Day >= 1 && Day <= DateTime.DaysInMonth(Year, Month);

    // Formats as "d MMMM yyyy" (e.g. "12 March 2025") for display, or an empty string when the
    // date is not a complete, valid answer — so summary/confirmation rendering never throws.
    public string ToDisplayString() =>
        IsCompleteDate ? $"{Day} {new DateTime(Year, Month, Day):MMMM yyyy}" : string.Empty;
}
