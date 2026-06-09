using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class RulesAdminViewModelFactoryTests
{
    private static RuleBranch Otherwise(string id) =>
        new(id, DecisionStatus.Scrutiny, Predicate.Otherwise.Instance);

    private static OutcomeRules Outcome(string key, string label, params RuleBranch[] branches) =>
        new(key, label, branches);

    private static RuleSet SampleRules() => new(
        "v3",
        new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
        new[]
        {
            Outcome("Inclusion", "Inclusion",
                new RuleBranch("INC-1", DecisionStatus.AutoApproved,
                    new Predicate.FieldEq("inclusionFlag", new FieldValue.Str("Y"))),
                Otherwise("INC-DEF")),
            Outcome("EAL", "English as an additional language", Otherwise("EAL-DEF"))
        });

    [Fact]
    public void RulesCard_summarises_version_count_and_latest_save()
    {
        var latest = new RulesConfigVersionDto
        {
            Id = 7, ConfigType = RulesConfigType.Rules, VersionNumber = 4,
            CreatedAt = new DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc), CreatedBy = "Ada"
        };

        var card = RulesAdminViewModelFactory.RulesCard(SampleRules(), latest);

        Assert.Equal("v3", card.Version);
        Assert.Equal(2, card.ItemCount);
        Assert.Equal(4, card.LatestVersionNumber);
        Assert.Equal("Ada", card.LastSavedBy);
        Assert.False(card.IsEmpty);
    }

    [Fact]
    public void RulesCard_empty_when_rules_null()
    {
        var card = RulesAdminViewModelFactory.RulesCard(null, null);
        Assert.True(card.IsEmpty);
        Assert.Equal(0, card.ItemCount);
    }

    [Fact]
    public void Outcomes_listing_maps_key_label_and_branch_count_in_order()
    {
        var vm = RulesAdminViewModelFactory.Outcomes(SampleRules());

        Assert.Equal(2, vm.Outcomes.Count);
        Assert.Equal("Inclusion", vm.Outcomes[0].Key);
        Assert.Equal("Inclusion", vm.Outcomes[0].Label);
        Assert.Equal(2, vm.Outcomes[0].BranchCount);
        Assert.Equal("EAL", vm.Outcomes[1].Key);
        Assert.Equal(1, vm.Outcomes[1].BranchCount);
    }

    [Fact]
    public void Outcome_detail_describes_each_branch_predicate()
    {
        var vm = RulesAdminViewModelFactory.Outcome(SampleRules(), "Inclusion");

        Assert.NotNull(vm);
        Assert.Equal("Inclusion", vm!.Key);
        Assert.Equal(2, vm.Branches.Count);
        Assert.Equal("INC-1", vm.Branches[0].Id);
        Assert.Equal(DecisionStatus.AutoApproved, vm.Branches[0].Status);
        Assert.Equal("inclusionFlag is \"Y\"", vm.Branches[0].Condition.Text);
        Assert.Equal("Otherwise (always matches)", vm.Branches[1].Condition.Text);
    }

    [Fact]
    public void Outcome_detail_returns_null_for_unknown_key()
    {
        Assert.Null(RulesAdminViewModelFactory.Outcome(SampleRules(), "DoesNotExist"));
    }

    [Fact]
    public void LookupsCard_counts_countries()
    {
        var lookups = new Lookups(new Dictionary<string, IReadOnlyList<string>>
        {
            ["GB"] = new[] { "English", "Welsh" },
            ["FR"] = new[] { "French" }
        });

        var card = RulesAdminViewModelFactory.LookupsCard(lookups, null);
        Assert.Equal(2, card.ItemCount);
        Assert.False(card.IsEmpty);
    }

    [Fact]
    public void Lookups_listing_sorts_rows_by_country_name_and_populates_name()
    {
        var lookups = new Lookups(new Dictionary<string, IReadOnlyList<string>>
        {
            // Codes deliberately out of name order: GB→"United Kingdom" sorts after FR→"France".
            ["GB"] = new[] { "English", "Welsh" },
            ["FR"] = new[] { "French" }
        });

        var vm = RulesAdminViewModelFactory.Lookups(lookups);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("FR", vm.Rows[0].CountryCode);
        Assert.Equal("France", vm.Rows[0].CountryName);
        Assert.Equal("GB", vm.Rows[1].CountryCode);
        Assert.Equal("United Kingdom", vm.Rows[1].CountryName);
        Assert.Equal("English, Welsh", vm.Rows[1].Languages);
    }

    [Fact]
    public void History_maps_versions_newest_first()
    {
        var versions = new List<RulesConfigVersionDto>
        {
            new() { Id = 1, ConfigType = RulesConfigType.Rules, VersionNumber = 1,
                    CreatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "A", Content = "{}" },
            new() { Id = 2, ConfigType = RulesConfigType.Rules, VersionNumber = 2,
                    CreatedAt = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "B", Content = "{}" }
        };

        var vm = RulesAdminViewModelFactory.History(RulesConfigType.Rules, versions);

        Assert.Equal(RulesConfigType.Rules, vm.ConfigType);
        Assert.Equal(2, vm.Versions.Count);
        Assert.Equal(2, vm.Versions[0].VersionNumber); // newest first
        Assert.Equal(1, vm.Versions[1].VersionNumber);
    }
}
