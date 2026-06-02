using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Pure edits to a <see cref="RuleSet"/> for the editor. The terminal
/// <see cref="Predicate.Otherwise"/> branch is always pinned last: inserts land
/// before it; it cannot be removed or moved past.
/// </summary>
public static class RuleSetSplicer
{
    public static RuleSet ReplaceBranch(RuleSet rules, string outcomeKey, string branchId, RuleBranch updated) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var i = list.FindIndex(b => b.Id == branchId);
            if (i >= 0) list[i] = updated;
            return list;
        });

    public static RuleSet InsertBranch(RuleSet rules, string outcomeKey, RuleBranch newBranch) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var otherwiseIndex = list.FindIndex(b => b.When is Predicate.Otherwise);
            if (otherwiseIndex < 0) list.Add(newBranch);
            else list.Insert(otherwiseIndex, newBranch);
            return list;
        });

    public static RuleSet RemoveBranch(RuleSet rules, string outcomeKey, string branchId) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var branch = list.FirstOrDefault(b => b.Id == branchId)
                ?? throw new InvalidOperationException($"Branch '{branchId}' not found.");
            if (branch.When is Predicate.Otherwise)
                throw new InvalidOperationException("The 'otherwise' branch cannot be removed.");
            list.Remove(branch);
            return list;
        });

    public static RuleSet MoveBranch(RuleSet rules, string outcomeKey, string branchId, bool up) =>
        MapOutcome(rules, outcomeKey, branches =>
        {
            var list = branches.ToList();
            var i = list.FindIndex(b => b.Id == branchId);
            if (i < 0 || list[i].When is Predicate.Otherwise) return list;

            var j = up ? i - 1 : i + 1;
            if (j < 0 || j >= list.Count) return list;
            if (list[j].When is Predicate.Otherwise) return list;

            (list[i], list[j]) = (list[j], list[i]);
            return list;
        });

    private static RuleSet MapOutcome(RuleSet rules, string key, Func<IReadOnlyList<RuleBranch>, List<RuleBranch>> edit)
    {
        var outcomes = rules.Outcomes.Select(o =>
            o.Key == key ? o with { Rules = edit(o.Rules) } : o).ToList();
        return rules with { Outcomes = outcomes };
    }
}
