namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A change type was selected on "What pupil data would you like to amend?", by the user or, when
/// the window type offers only one, by the service. The journey starts. No reference number exists yet.
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
