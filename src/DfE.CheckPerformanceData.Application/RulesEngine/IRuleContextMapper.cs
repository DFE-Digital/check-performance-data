using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Projects a queue <see cref="RequestDocument"/> into a typed
/// <see cref="RuleContext"/> for the engine. Pure (depends only on the document,
/// the static field catalogue, and the answer-field map).
/// </summary>
public interface IRuleContextMapper
{
    /// <summary>
    /// Build a context. Throws when an answer's <c>Value</c> is present but cannot
    /// be parsed into the catalogue's expected type — the worker treats that as
    /// a synthetic Scrutiny per the fallback policy.
    /// </summary>
    RuleContext Map(RequestDocument document);
}
