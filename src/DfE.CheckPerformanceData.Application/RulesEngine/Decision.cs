namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// The result of evaluating the rules engine against a single request.
/// Carries enough trace information for the human auditor (and the Zendesk ticket
/// description) to reproduce why a particular outcome was reached.
/// </summary>
/// <param name="Status">Auto-decision outcome the worker should act on.</param>
/// <param name="OutcomeKey">
/// The outcome bucket the engine resolved the request into (e.g.
/// <c>ElectiveHomeEducation</c>). <c>_unknown</c> when the request's reason
/// did not match any configured outcome.
/// </param>
/// <param name="MatchedRuleId">
/// Identifier of the specific <see cref="RuleBranch"/> whose predicate evaluated true.
/// Synthetic values (prefixed with <c>_</c>) indicate the decision came from a
/// fallback path rather than a real rule branch.
/// </param>
/// <param name="Trace">
/// Ordered, human-readable lines describing which leaf predicates were evaluated
/// and what they returned. Capped to keep ticket descriptions readable.
/// </param>
public sealed record Decision(
    DecisionStatus Status,
    string OutcomeKey,
    string MatchedRuleId,
    IReadOnlyList<string> Trace)
{
    /// <summary>
    /// Used when the request's reason does not match any configured outcome.
    /// Per the design's "always Scrutiny on doubt" policy, this becomes a Scrutiny
    /// ticket so a human reviewer sees it.
    /// </summary>
    public static Decision UnmatchedOutcome(string outcomeKey) =>
        new(DecisionStatus.Scrutiny, outcomeKey, "_unmatched_outcome",
            [$"No outcome configured for key '{outcomeKey}'"]);

    /// <summary>
    /// Reached only if validation is bypassed and an outcome has no terminal
    /// <c>otherwise</c>. The validator should make this unreachable in practice.
    /// </summary>
    public static Decision NoMatch(string outcomeKey, IReadOnlyList<string> trace) =>
        new(DecisionStatus.Scrutiny, outcomeKey, "_no_match", trace);

    /// <summary>
    /// Fallback constructed by the worker when the mapper or engine itself throws.
    /// </summary>
    public static Decision SyntheticScrutiny(string syntheticRuleId, string detail) =>
        new(DecisionStatus.Scrutiny, "_unknown", syntheticRuleId, [detail]);
}
