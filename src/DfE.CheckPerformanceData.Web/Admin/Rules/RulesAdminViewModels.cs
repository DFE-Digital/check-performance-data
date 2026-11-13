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

    /// <summary>No file yet, or a file with no usable data — offer an upload escape hatch.</summary>
    public bool CanUpload { get; init; }

    /// <summary>At least one saved version exists — show the version history link.</summary>
    public bool HasHistory { get; init; }
}

public sealed record RulesLandingViewModel
{
    public required RulesConfigCardViewModel Rules { get; init; }
    public required RulesConfigCardViewModel Lookups { get; init; }
}

/// <summary>Backs the upload page for a single config blob.</summary>
public sealed record RulesUploadViewModel
{
    public required RulesConfigType Type { get; init; }
    public required string Title { get; init; }      // e.g. "Upload rules.json"
    public required string FileName { get; init; }   // "rules.json" / "country-languages.json"
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed record OutcomeSummaryViewModel(string Key, string Label, int BranchCount);

public sealed record OutcomesViewModel
{
    public required IReadOnlyList<OutcomeSummaryViewModel> Outcomes { get; init; }
    public bool IsEmpty => Outcomes.Count == 0;
}

public sealed record BranchViewModel(string Id, DecisionStatus Status, PredicateNode Condition, bool IsOtherwise);

/// <summary>
/// #800: the read-only "rules for this window" page. Every branch condition is already
/// described with the window-aware describer, so the view renders nodes as-is.
/// </summary>
public sealed record WindowRulesViewModel
{
    /// <summary>The page heading: the window title with "rules" — shown in the one &lt;h1&gt;.</summary>
    public required string Title { get; init; }

    /// <summary>The window title alone, for the breadcrumb and the back link.</summary>
    public required string WindowTitle { get; init; }

    public required Guid WindowId { get; init; }

    /// <summary>The window's start date — the anchor a window-relative date resolves from.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>The serving rules file's version, or null when no rules are configured.</summary>
    public string? RulesVersion { get; init; }

    /// <summary>False when the rules blob is missing or holds no outcomes: show the empty state.</summary>
    public bool HasRules { get; init; }

    public required IReadOnlyList<WindowOutcomeRules> Outcomes { get; init; }
}

/// <summary>#800: one outcome card on the window rules page.</summary>
public sealed record WindowOutcomeRules(string Label, IReadOnlyList<BranchViewModel> Branches);

public sealed record OutcomeDetailViewModel
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<BranchViewModel> Branches { get; init; }
}

public sealed record LookupRowViewModel(string CountryCode, string CountryName, string Languages);

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
