using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>Pure mapping from domain config types to read-only admin view models.</summary>
public static class RulesAdminViewModelFactory
{
    public static RulesConfigCardViewModel RulesCard(RuleSet? rules, RulesConfigVersionDto? latest)
    {
        if (rules is null)
        {
            return new RulesConfigCardViewModel { IsEmpty = true, ItemNoun = "outcomes", CanUpload = true };
        }

        return new RulesConfigCardViewModel
        {
            IsEmpty = false,
            Version = rules.Version,
            ItemCount = rules.Outcomes.Count,
            ItemNoun = "outcomes",
            LatestVersionNumber = latest?.VersionNumber,
            LastSavedAt = latest?.CreatedAt,
            LastSavedBy = latest?.CreatedBy,
            CanUpload = rules.Outcomes.Count == 0,
            HasHistory = latest is not null
        };
    }

    public static RulesConfigCardViewModel LookupsCard(Lookups? lookups, RulesConfigVersionDto? latest)
    {
        if (lookups is null)
        {
            return new RulesConfigCardViewModel { IsEmpty = true, ItemNoun = "countries", CanUpload = true };
        }

        return new RulesConfigCardViewModel
        {
            IsEmpty = false,
            ItemCount = lookups.CountryLanguages.Count,
            ItemNoun = "countries",
            LatestVersionNumber = latest?.VersionNumber,
            LastSavedAt = latest?.CreatedAt,
            LastSavedBy = latest?.CreatedBy,
            CanUpload = lookups.CountryLanguages.Count == 0,
            HasHistory = latest is not null
        };
    }

    public static OutcomesViewModel Outcomes(RuleSet? rules) => new()
    {
        Outcomes = rules is null
            ? Array.Empty<OutcomeSummaryViewModel>()
            : rules.Outcomes
                .Select(o => new OutcomeSummaryViewModel(o.Key, o.Label, o.Rules.Count))
                .ToList()
    };

    public static OutcomeDetailViewModel? Outcome(RuleSet? rules, string key)
    {
        var outcome = rules?.Outcomes.FirstOrDefault(o =>
            string.Equals(o.Key, key, StringComparison.Ordinal));
        if (outcome is null) return null;

        return new OutcomeDetailViewModel
        {
            Key = outcome.Key,
            Label = outcome.Label,
            Branches = outcome.Rules
                .Select(b => MapBranch(b, pupilDataStartDate: null))
                .ToList()
        };
    }

    /// <summary>
    /// #800: the window rules page. Every branch condition is described with the
    /// window-aware describer, resolving window-relative dates against the window's
    /// PupilData exercise start date. Branches whose <c>checkingWindowType</c> condition
    /// can never match this window's type are hidden: their dates do not apply here.
    /// A missing or empty rules set yields the "no rules configured" state.
    /// </summary>
    public static WindowRulesViewModel WindowRules(
        string windowTitle, Guid windowId, DateOnly? pupilDataStartDate,
        CheckingWindowType windowType, RuleSet? rules) => new()
    {
        Title = $"{windowTitle} rules",
        WindowTitle = windowTitle,
        WindowId = windowId,
        StartDate = pupilDataStartDate,
        WindowType = windowType,
        RulesVersion = rules?.Version,
        HasRules = rules is not null && rules.Outcomes.Count > 0,
        Outcomes = rules is null
            ? Array.Empty<WindowOutcomeRules>()
            : rules.Outcomes
                .Select(o => new WindowOutcomeRules(o.Label,
                    o.Rules
                        .Where(b => AppliesToWindowType(b.When, windowType))
                        .Select(b => MapBranch(b, pupilDataStartDate))
                        .ToList()))
                .ToList()
    };

    /// <summary>Maps one branch to its view model, describing the condition with the
    /// window-aware describer when an anchor date is available.</summary>
    private static BranchViewModel MapBranch(RuleBranch branch, DateOnly? pupilDataStartDate) =>
        new(branch.Id, branch.Status, PredicateDescriber.Describe(branch.When, pupilDataStartDate),
            branch.When is Predicate.Otherwise);

    /// <summary>
    /// True when a branch's <c>checkingWindowType</c> condition can match
    /// <paramref name="windowType"/>. Only equality tests can prove the opposite: a branch
    /// is hidden when a direct <c>checkingWindowType</c> <c>eq</c> or <c>in</c> test (or one
    /// such test inside the branch's top-level <c>all</c>) lists values that all exclude
    /// the window's type. <c>neq</c> and comparison shapes are kept (fail safe): they cannot
    /// prove a branch never applies, so showing it is the safe direction.
    /// </summary>
    private static bool AppliesToWindowType(Predicate when, CheckingWindowType windowType)
    {
        var target = AnswerFieldMap.NormaliseCheckingWindowType(windowType.ToString());

        if (when is Predicate.AllOf { Items: { Count: > 0 } } allOf)
        {
            return allOf.Items.All(i => AppliesToWindowType(i, windowType));
        }

        // Only eq and in can prove a branch never applies. A field test on another field,
        // a neq, or a comparison cannot, so the branch stays visible.
        var test = when switch
        {
            Predicate.FieldEq f when f.Field == "checkingWindowType" => new[] { f.Value },
            Predicate.FieldIn f when f.Field == "checkingWindowType" => f.Values,
            _ => null
        };
        if (test is null) return true;

        var constrained = test
            .OfType<FieldValue.Str>()
            .Select(v => AnswerFieldMap.NormaliseCheckingWindowType(v.Value))
            .ToHashSet(StringComparer.Ordinal);
        return constrained.Count == 0 || constrained.Contains(target);
    }

    public static LookupsViewModel Lookups(Lookups? lookups) => new()
    {
        Rows = lookups is null
            ? Array.Empty<LookupRowViewModel>()
            : lookups.CountryLanguages
                .Select(kv => new LookupRowViewModel(
                    kv.Key, CountryNames.DisplayName(kv.Key), string.Join(", ", kv.Value)))
                .OrderBy(r => r.CountryName, StringComparer.CurrentCulture)
                .ThenBy(r => r.CountryCode, StringComparer.Ordinal)
                .ToList()
    };

    public static HistoryViewModel History(RulesConfigType type, IReadOnlyList<RulesConfigVersionDto> versions) => new()
    {
        ConfigType = type,
        Versions = versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new VersionRowViewModel(v.Id, v.VersionNumber, v.CreatedAt, v.CreatedBy))
            .ToList()
    };

    public static RulesUploadViewModel Upload(RulesConfigType type, IReadOnlyList<string>? errors = null)
    {
        var fileName = type == RulesConfigType.Rules ? "rules.json" : "country-languages.json";
        return new RulesUploadViewModel
        {
            Type = type,
            FileName = fileName,
            Title = $"Upload {fileName}",
            Errors = errors ?? Array.Empty<string>()
        };
    }

    public static VersionDetailViewModel VersionDetail(RulesConfigVersionDto dto) => new()
    {
        ConfigType = dto.ConfigType,
        VersionNumber = dto.VersionNumber,
        CreatedAt = dto.CreatedAt,
        CreatedBy = dto.CreatedBy,
        Content = dto.Content
    };
}
