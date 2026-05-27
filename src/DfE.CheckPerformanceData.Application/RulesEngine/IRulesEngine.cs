namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Pure evaluator. No IO, no statics aside from any injected
/// <see cref="TimeProvider"/>. Safe to register as a singleton.
/// </summary>
public interface IRulesEngine
{
    /// <summary>
    /// Walk <paramref name="rules"/> for the outcome named by <paramref name="ctx"/>
    /// and return the first matching branch's <see cref="Decision"/>.
    ///
    /// Returns <see cref="Decision.UnmatchedOutcome"/> if no outcome key matches.
    /// Returns <see cref="Decision.NoMatch"/> if the outcome lacks a terminal
    /// <c>otherwise</c> (the validator should make this unreachable).
    /// </summary>
    Decision Evaluate(RuleSet rules, RuleContext ctx, Lookups lookups);
}
