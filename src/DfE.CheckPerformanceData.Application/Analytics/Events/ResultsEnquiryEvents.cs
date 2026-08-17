namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A school began a 16-19 results enquiry (AB#296648) — chose an issue type on the ResultIssue page.
/// Paired with <see cref="ResultsEnquirySubmittedEvent"/> to give the start-to-submit funnel, which is
/// how we will find out whether the late-results guidance is stopping enquiries that did not need
/// raising.
/// </summary>
public sealed record ResultsEnquiryStartedEvent : AnalyticsEvent
{
    /// <summary>The issue type chosen, e.g. <c>incorrect-grade</c>. An option value, never free text.</summary>
    public required string EnquiryType { get; init; }

    public required string CheckingWindowType { get; init; }

    /// <summary>
    /// Whether the school had to be shown the "check your second late results file" guidance. This is
    /// the measure that tells us whether the guidance is doing its job.
    /// </summary>
    public required bool LateResultsGuidanceShown { get; init; }

    public override string EventType => "results_enquiry_started";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("enquiry_type", EnquiryType),
        new("checking_window_type", CheckingWindowType),
        new("late_results_guidance_shown", LateResultsGuidanceShown.ToString().ToLowerInvariant()),
    ];
}

/// <summary>
/// A 16-19 results enquiry was submitted (AB#296648).
///
/// House law on PII: the reference number is always hidden, and no grade, QAN, student name or free
/// text ever appears — a grade paired with a school and a date is identifying, and the comments box is
/// free text by definition.
/// </summary>
public sealed record ResultsEnquirySubmittedEvent : AnalyticsEvent
{
    public required string EnquiryType { get; init; }

    /// <summary>Whether the enquiry covers a whole cohort. A count would be safe too, but the
    /// boolean is what tells us how often the cohort branch is used.</summary>
    public required bool CohortWide { get; init; }

    public required string CheckingWindowType { get; init; }
    public required string ReferenceNumber { get; init; }

    public override string EventType => "results_enquiry_submitted";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("enquiry_type", EnquiryType),
        new("cohort_wide", CohortWide.ToString().ToLowerInvariant()),
        new("checking_window_type", CheckingWindowType),
        new("reference_number", ReferenceNumber, Hidden: true),
    ];
}
