namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// The user submitted the "pupil data is correct" declaration (the successful POST on
/// the Confirm Correct page, after the confirmation is persisted). The reference number
/// is hidden (masked) pending the DPIA classification.
/// </summary>
public sealed record CorrectDataConfirmedEvent : AnalyticsEvent
{
    public required string ReferenceNumber { get; init; }
    public required string CheckingWindowType { get; init; }

    public override string EventType => "correct_data_confirmed";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("reference_number", ReferenceNumber, Hidden: true),
        new("checking_window_type", CheckingWindowType),
    ];
}
