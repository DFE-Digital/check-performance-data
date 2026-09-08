namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// AB#298317: how the next-opportunity date reads on screen — month and year only ("October
/// 2027"). One place, because the landing banner, Check your pupil data and the admin Summary all
/// print it and must agree. Checking-window dates are UK wall-clock values, so the value is
/// formatted as it stands and never routed through <c>LondonTime</c>.
/// </summary>
public static class NextOpportunityText
{
    public const string Format = "MMMM yyyy";

    /// <summary>Null when not set, so a caller can omit the sentence rather than print "in ".</summary>
    public static string? For(DateTime? nextOpportunity) => nextOpportunity?.ToString(Format);
}
