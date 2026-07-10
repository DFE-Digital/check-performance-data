namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A user submitted the Contact Us wayfinder form (valid enquiry-type selection). Carries only the
/// selected enquiry type (a stable, non-PII category) and whether the user was signed in — never
/// the name/email/school/message inputs, which are neither validated nor persisted.
/// </summary>
public sealed record ContactUsSubmittedEvent : AnalyticsEvent
{
    public required string EnquiryType { get; init; }
    public required bool IsAuthenticated { get; init; }

    public override string EventType => "contact_us_submitted";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("enquiry_type", EnquiryType),
        new("is_authenticated", IsAuthenticated),
    ];
}
