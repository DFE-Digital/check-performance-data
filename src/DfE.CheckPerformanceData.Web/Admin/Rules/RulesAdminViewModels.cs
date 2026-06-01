using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>Summary card on the landing page for one config blob.</summary>
public sealed record RulesConfigCardViewModel
{
    public bool IsEmpty { get; init; }
    public string? Version { get; init; }
    public int ItemCount { get; init; }            // outcomes (rules) or countries (lookups)
    public string ItemNoun { get; init; } = string.Empty;
    public int? LatestVersionNumber { get; init; }
    public DateTime? LastSavedAt { get; init; }
    public string? LastSavedBy { get; init; }
}

public sealed record RulesLandingViewModel
{
    public required RulesConfigCardViewModel Rules { get; init; }
    public required RulesConfigCardViewModel Lookups { get; init; }
}

public sealed record OutcomeSummaryViewModel(string Key, string Label, int BranchCount);

public sealed record OutcomesViewModel
{
    public required IReadOnlyList<OutcomeSummaryViewModel> Outcomes { get; init; }
    public bool IsEmpty => Outcomes.Count == 0;
}

public sealed record BranchViewModel(string Id, DecisionStatus Status, PredicateNode Condition);

public sealed record OutcomeDetailViewModel
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<BranchViewModel> Branches { get; init; }
}

public sealed record LookupRowViewModel(string CountryCode, string Languages);

public sealed record LookupsViewModel
{
    public required IReadOnlyList<LookupRowViewModel> Rows { get; init; }
    public bool IsEmpty => Rows.Count == 0;
}

public sealed record VersionRowViewModel(int Id, int VersionNumber, DateTime CreatedAt, string? CreatedBy);

public sealed record HistoryViewModel
{
    public required RulesConfigType ConfigType { get; init; }
    public required IReadOnlyList<VersionRowViewModel> Versions { get; init; }
}

public sealed record VersionDetailViewModel
{
    public required RulesConfigType ConfigType { get; init; }
    public required int VersionNumber { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public required string Content { get; init; }
}
