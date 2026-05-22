namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Top-level rules document loaded from blob storage. One <see cref="OutcomeRules"/>
/// per docx "request outcome" category (Inclusion, Deceased, Elective home education,
/// etc.).
/// </summary>
public sealed record RuleSet(
    string Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OutcomeRules> Outcomes);

/// <summary>
/// All branches for a single outcome category. The evaluator walks
/// <see cref="Rules"/> top-to-bottom; the first branch whose
/// <see cref="RuleBranch.When"/> evaluates true wins.
/// </summary>
public sealed record OutcomeRules(
    string Key,
    string Label,
    IReadOnlyList<RuleBranch> Rules);

/// <summary>
/// One row in an outcome's decision table.
/// </summary>
/// <param name="Id">
/// Stable identifier used in audit traces and Zendesk tickets (e.g. <c>EHE-KS4</c>).
/// Required so business edits can be tracked.
/// </param>
public sealed record RuleBranch(
    string Id,
    DecisionStatus Status,
    Predicate When);
