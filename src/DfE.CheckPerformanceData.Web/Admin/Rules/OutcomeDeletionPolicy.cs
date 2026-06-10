using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Decides whether a rule outcome may be deleted. An outcome the form can still route to
/// (its key appears in <see cref="AnswerFieldMap.WhatToChangeToOutcomeKey"/>) must not be
/// deleted, because the form could still produce a request targeting it.
/// </summary>
public static class OutcomeDeletionPolicy
{
    public static bool IsFormBound(string outcomeKey) =>
        AnswerFieldMap.WhatToChangeToOutcomeKey.Values.Contains(outcomeKey, StringComparer.OrdinalIgnoreCase);
}
