using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Domain.Enums;
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
        Assert.False(card.CanUpload);   // has data
        Assert.True(card.HasHistory);   // has a saved version
    }

    [Fact]
    public void RulesCard_empty_when_rules_null()
    {
        var card = RulesAdminViewModelFactory.RulesCard(null, null);
        Assert.True(card.IsEmpty);
        Assert.Equal(0, card.ItemCount);
        Assert.True(card.CanUpload);    // no file → offer upload
        Assert.False(card.HasHistory);  // nothing saved yet
    }

    [Fact]
    public void RulesCard_can_upload_when_present_but_zero_outcomes()
    {
        var empty = new RuleSet("v0", new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), Array.Empty<OutcomeRules>());

        var card = RulesAdminViewModelFactory.RulesCard(empty, null);

        Assert.False(card.IsEmpty);     // a file exists...
        Assert.True(card.CanUpload);    // ...but it has no data → still offer upload
        Assert.False(card.HasHistory);
    }

    [Fact]
    public void LookupsCard_can_upload_when_present_but_zero_countries()
    {
        var card = RulesAdminViewModelFactory.LookupsCard(Lookups.Empty, null);

        Assert.False(card.IsEmpty);
        Assert.True(card.CanUpload);
        Assert.False(card.HasHistory);
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

    // #800: the window rules page view model.

    [Fact]
    public void WindowRules_carries_window_metadata_and_versions()
    {
        var windowId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 9, 1);

        var vm = RulesAdminViewModelFactory.WindowRules(
            "Main 2027", windowId, startDate, CheckingWindowType.KS4June, SampleRules());

        Assert.Equal("Main 2027 rules", vm.Title);
        Assert.Equal("Main 2027", vm.WindowTitle);
        Assert.Equal(windowId, vm.WindowId);
        Assert.Equal(startDate, vm.StartDate);
        Assert.Equal(CheckingWindowType.KS4June, vm.WindowType);
        Assert.Equal("v3", vm.RulesVersion);
        Assert.True(vm.HasRules);
    }

    [Fact]
    public void WindowRules_hides_branches_for_other_window_types()
    {
        // A KS4June window: a branch gated on checkingWindowType == KS2 never applies, so it
        // is hidden. A branch with no window-type gate, and one gated on the window's own
        // type, both stay.
        var rules = new RuleSet("v1", new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            new[]
            {
                Outcome("Inclusion", "Inclusion",
                    new RuleBranch("INC-KS2", DecisionStatus.AutoApproved,
                        new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2"))),
                    new RuleBranch("INC-KS4", DecisionStatus.AutoApproved,
                        new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June"))),
                    Otherwise("INC-DEF"))
            });

        var vm = RulesAdminViewModelFactory.WindowRules(
            "Main 2027", Guid.NewGuid(), new DateOnly(2026, 9, 1),
            CheckingWindowType.KS4June, rules);

        var ids = vm.Outcomes.Single().Branches.Select(b => b.Id).ToHashSet();
        Assert.DoesNotContain("INC-KS2", ids);
        Assert.Contains("INC-KS4", ids);
        Assert.Contains("INC-DEF", ids);
    }

    [Fact]
    public void WindowRules_keeps_neq_branches_even_when_they_exclude_the_window_type()
    {
        // A neq test cannot prove a branch never applies, so it is never hidden. On a
        // Post16 window the branch "checkingWindowType neq Post16" does not apply, but the
        // page still shows it (fail safe). An eq branch for another type is hidden, and a
        // neq branch that does apply is shown.
        var rules = new RuleSet("v1", new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            new[]
            {
                Outcome("Nor", "Nor",
                    new RuleBranch("NOR-NONPOST16", DecisionStatus.AutoApproved,
                        new Predicate.FieldNeq("checkingWindowType", new FieldValue.Str("Post16"))),
                    new RuleBranch("NOR-NEQKS2", DecisionStatus.AutoApproved,
                        new Predicate.FieldNeq("checkingWindowType", new FieldValue.Str("KS2"))),
                    new RuleBranch("NOR-KS2", DecisionStatus.AutoApproved,
                        new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS2"))),
                    Otherwise("NOR-DEF"))
            });

        var vm = RulesAdminViewModelFactory.WindowRules(
            "Main 2027", Guid.NewGuid(), new DateOnly(2026, 9, 1),
            CheckingWindowType.Post16, rules);

        var ids = vm.Outcomes.Single().Branches.Select(b => b.Id).ToHashSet();
        Assert.Contains("NOR-NONPOST16", ids);  // neq of this type: does not apply, but shown
        Assert.Contains("NOR-NEQKS2", ids);     // neq of another type: applies, shown
        Assert.DoesNotContain("NOR-KS2", ids);  // eq of another type: never applies, hidden
        Assert.Contains("NOR-DEF", ids);
    }

    [Fact]
    public void WindowRules_keeps_comparison_branches_even_when_the_value_differs()
    {
        // A comparison (lt/gt) on the window type is not an equality test, so it cannot
        // prove that a branch does not apply. The branch stays visible.
        var rules = new RuleSet("v1", new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            new[]
            {
                Outcome("Nor", "Nor",
                    new RuleBranch("NOR-GE", DecisionStatus.AutoApproved,
                        new Predicate.FieldCompare("checkingWindowType", CompareOp.Gte, new FieldValue.Str("KS2"))),
                    Otherwise("NOR-DEF"))
            });

        var vm = RulesAdminViewModelFactory.WindowRules(
            "Main 2027", Guid.NewGuid(), new DateOnly(2026, 9, 1),
            CheckingWindowType.Post16, rules);

        var ids = vm.Outcomes.Single().Branches.Select(b => b.Id).ToHashSet();
        Assert.Contains("NOR-GE", ids);
        Assert.Contains("NOR-DEF", ids);
    }

    [Fact]
    public void WindowRules_describes_each_branch_with_the_window_relative_date()
    {
        var rules = new RuleSet("v1", new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            new[]
            {
                Outcome("Inclusion", "Inclusion",
                    new RuleBranch("INC-1", DecisionStatus.AutoApproved,
                        new Predicate.FieldEq("dateOfRemoval",
                            new FieldValue.RelativeDate(new RelativeDateSpec.SecondMondayOfMay()))))
            });

        var vm = RulesAdminViewModelFactory.WindowRules("Main 2027", Guid.NewGuid(),
            new DateOnly(2026, 9, 1), CheckingWindowType.KS2, rules);

        // The second Monday of May 2026 is 11 May 2026; the anchor is the PupilData
        // exercise start-date year, not the year the page is viewed in.
        var branch = vm.Outcomes.Single().Branches.Single();
        Assert.Contains("11 May 2026", branch.Condition.Text);
        Assert.Contains("window-relative: second Monday of May", branch.Condition.Text);
    }

    [Fact]
    public void WindowRules_leaves_the_start_date_null_when_the_window_has_no_pupil_data()
    {
        // A window without a PupilData exercise has no anchor: the view model carries a null
        // start date and the view renders it as "not set", never a guess.
        var vm = RulesAdminViewModelFactory.WindowRules(
            "Main 2027", Guid.NewGuid(), null, CheckingWindowType.KS4June, null);

        Assert.Null(vm.StartDate);
        Assert.False(vm.HasRules);
    }

    [Fact]
    public void WindowRules_reports_no_rules_when_null_or_empty()
    {
        var windowId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 9, 1);

        var missing = RulesAdminViewModelFactory.WindowRules(
            "Main 2027", windowId, startDate, CheckingWindowType.KS4June, null);
        Assert.False(missing.HasRules);
        Assert.Null(missing.RulesVersion);
        Assert.Empty(missing.Outcomes);

        var empty = RulesAdminViewModelFactory.WindowRules("Main 2027", windowId, startDate,
            CheckingWindowType.KS4June,
            new RuleSet("v0", new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), Array.Empty<OutcomeRules>()));
        Assert.False(empty.HasRules);
        Assert.Empty(empty.Outcomes);
    }
}
