namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// Emitted by the rules-engine worker after each request is evaluated, capturing the
/// decision mix (auto-approve vs scrutiny vs reject) and how it was reached. Carries no
/// PII — only decision metadata.
/// </summary>
public sealed record RequestDecisionEvent : AnalyticsEvent
{
    public required string DecisionStatus { get; init; }
    public required string OutcomeKey { get; init; }
    public string? MatchedRuleId { get; init; }
    public required string RulesVersion { get; init; }
    public required string RequestTypeCode { get; init; }
    public required string CheckingWindowType { get; init; }
    public bool IsSyntheticFallback { get; init; }

    public override string EventType => "request_decision";

    public override IReadOnlyList<AnalyticsField> Fields =>
    [
        new("decision_status", DecisionStatus),
        new("outcome_key", OutcomeKey),
        new("matched_rule_id", MatchedRuleId),
        new("rules_version", RulesVersion),
        new("request_type_code", RequestTypeCode),
        new("checking_window_type", CheckingWindowType),
        new("is_synthetic_fallback", IsSyntheticFallback),
    ];
}
