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
                .Select(b => new BranchViewModel(b.Id, b.Status, PredicateDescriber.Describe(b.When), b.When is Predicate.Otherwise))
                .ToList()
        };
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
