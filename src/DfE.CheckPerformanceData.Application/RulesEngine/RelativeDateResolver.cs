namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Turns a window-relative date form into a concrete date from the checking
/// window's start date (#800). A pure function: the current date and time are
/// never used, so the same window start date always gives the same date.
/// </summary>
public static class RelativeDateResolver
{
    /// <summary>
    /// Resolves <paramref name="spec"/> against <paramref name="windowStartDate"/>.
    /// Returns false when the window start date is null, the form has no date in
    /// the window's year (for example 29 February in a non-leap year), or the
    /// offset lands before year 1 (for example a 3-year offset from a year-1 start).
    /// </summary>
    public static bool TryResolve(RelativeDateSpec spec, DateOnly? windowStartDate, out DateOnly date)
    {
        date = default;
        if (spec is null || windowStartDate is null) return false;

        var year = windowStartDate.Value.Year;
        switch (spec)
        {
            case RelativeDateSpec.DayMonthOfWindowYear d:
            {
                if (d.Month is < 1 or > 12) return false;
                var maxDay = DateTime.DaysInMonth(year, d.Month);
                if (d.Day < 1 || d.Day > maxDay) return false;
                date = new DateOnly(year, d.Month, d.Day);
                return true;
            }

            case RelativeDateSpec.FirstSeptemberMinusYears y:
                // DateOnly has no year 0: a start date whose year is not far enough
                // ahead of the offset has no date (for example year 1 with a 3-year spec).
                if (year - y.Years < 1) return false;
                date = new DateOnly(year - y.Years, 9, 1);
                return true;

            case RelativeDateSpec.SecondMondayOfMay:
            {
                var first = new DateOnly(year, 5, 1);
                var daysToFirstMonday = (8 - (int)first.DayOfWeek) % 7;
                date = first.AddDays(daysToFirstMonday + 7);
                return true;
            }

            default:
                return false;
        }
    }
}
