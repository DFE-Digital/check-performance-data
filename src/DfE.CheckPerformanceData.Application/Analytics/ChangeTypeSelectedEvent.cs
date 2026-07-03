namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// The user chose what pupil data to change (the valid POST on the "what to change"
/// step), entering a journey. No reference number exists yet at this point.
/// </summary>
public sealed record ChangeTypeSelectedEvent : AnalyticsEvent
{
    public required string WhatToChange { get; init; }
    public required string CheckingWindowType { get; init; }

    public override string EventType => "change_type_selected";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("what_to_change", WhatToChange),
        new("checking_window_type", CheckingWindowType),
    ];
}
