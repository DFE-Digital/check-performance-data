namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A journey form POST failed validation. Carries the error count and a controlled,
/// machine-readable code taxonomy — never the raw validation messages, which are
/// free text and can embed the pupil's name.
/// </summary>
public sealed record ValidationErrorEvent : AnalyticsEvent
{
    public required int ErrorCount { get; init; }
    public required IReadOnlyList<string> ErrorCodes { get; init; }
    public string? WhatToChange { get; init; }
    public bool? FromSummary { get; init; }

    public override string EventType => "validation_error";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("error_count", ErrorCount),
        new("error_codes", ErrorCodes),
        new("what_to_change", WhatToChange),
        new("from_summary", FromSummary),
    ];
}
