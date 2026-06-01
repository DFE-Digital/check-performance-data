namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Describes a violation of a page's "at least one question must be answered" rule.
/// <see cref="SummaryMessage"/> is the lead-in copy for the error summary;
/// <see cref="FieldErrors"/> holds the per-field error message keyed by question id.
/// </summary>
public sealed record RequireAtLeastOneResult(
    string SummaryMessage,
    IReadOnlyDictionary<string, string> FieldErrors);
