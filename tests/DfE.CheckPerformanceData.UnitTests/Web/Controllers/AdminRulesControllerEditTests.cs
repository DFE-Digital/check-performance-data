using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class AdminRulesControllerEditTests
{
    private static RuleSet Rules() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("EAL", "EAL", new[]
        {
            new RuleBranch("EAL-1", DecisionStatus.Scrutiny, new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))),
            new RuleBranch("EAL-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
        })
    });

    private static IRulesConfigService SvcWithRules(string etag = "etag-1")
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((Rules(), etag));
        return svc;
    }

    private static AdminRulesController NewController(IRulesConfigService svc)
    {
        var controller = new AdminRulesController(svc);
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(),
            Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static BranchEditForm SaveableForm(string etag) => new()
    {
        OutcomeKey = "EAL", BranchId = "EAL-1", IsNew = false, Status = DecisionStatus.AutoApproved,
        LoadETag = etag, Action = "save",
        Nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
        }
    };

    [Fact]
    public async Task EditBranch_Loads_Form_With_Captured_ETag()
    {
        var result = await NewController(SvcWithRules("etag-xyz")).EditBranch("EAL", "EAL-1", default);

        var vm = Assert.IsType<BranchEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("EAL-1", vm.Form.BranchId);
        Assert.False(vm.Form.IsNew);
        Assert.Equal("etag-xyz", vm.Form.LoadETag);
        // A bare-leaf branch is wrapped in an AllOf root for editing so the editor shows
        // the Add condition/Add group affordances (collapsed back to the leaf on save).
        Assert.Equal(2, vm.Form.Nodes.Count);
        Assert.Equal(PredicateKind.AllOf, vm.Form.Nodes.Single(n => n.ParentId is null).Kind);
    }

    [Fact]
    public async Task EditBranch_NotFound_For_Unknown_Branch()
    {
        var result = await NewController(SvcWithRules()).EditBranch("EAL", "nope", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditBranch_Refuses_Otherwise_Branch()
    {
        var result = await NewController(SvcWithRules()).EditBranch("EAL", "EAL-OTHER", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AddBranch_Seeds_New_Form_With_Empty_AllOf()
    {
        var result = await NewController(SvcWithRules()).AddBranch("EAL", default);

        var vm = Assert.IsType<BranchEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.Form.IsNew);
        Assert.Single(vm.Form.Nodes);
        Assert.Equal(PredicateKind.AllOf, vm.Form.Nodes[0].Kind);
        Assert.StartsWith("EAL-", vm.Form.BranchId);
        Assert.Equal(DecisionStatus.Scrutiny, vm.Form.Status);
    }

    [Fact]
    public async Task Transform_AddCondition_ReRenders_Without_Persisting()
    {
        var svc = SvcWithRules();
        var form = new BranchEditForm
        {
            OutcomeKey = "EAL", BranchId = "EAL-1", Status = DecisionStatus.Scrutiny, LoadETag = "etag-1",
            Action = "addCondition:1",
            Nodes = new List<PredicateNodeForm> { new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf } }
        };

        var result = await NewController(svc).TransformBranch(form, default);

        var vm = Assert.IsType<BranchEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, vm.Form.Nodes.Count);
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transform_Rerender_Preserves_Bound_Combinator_Kind()
    {
        var svc = SvcWithRules();
        var form = new BranchEditForm
        {
            OutcomeKey = "EAL", BranchId = "EAL-1", LoadETag = "etag-1", Action = "rerender",
            Nodes = new List<PredicateNodeForm>
            {
                new() { Id = 1, ParentId = null, Kind = PredicateKind.AnyOf }, // user switched AllOf -> AnyOf via the bound select
                new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "KS4" },
            }
        };

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).TransformBranch(form, default)).Model);

        Assert.Equal(PredicateKind.AnyOf, vm.Form.Nodes.Single(n => n.Id == 1).Kind); // not reverted
    }

    [Fact]
    public async Task Save_Persists_And_Redirects_On_Success()
    {
        var svc = SvcWithRules("etag-1");
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(5));

        var result = await NewController(svc).SaveBranch(SaveableForm("etag-1"), default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Outcome", redirect.ActionName);
        await svc.Received(1).SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_Blocks_On_Empty_Composite_Without_Calling_Service()
    {
        var svc = SvcWithRules("etag-1");
        var form = SaveableForm("etag-1");
        form.Nodes = new List<PredicateNodeForm> { new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf } };

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(form, default)).Model);

        Assert.NotEmpty(vm.Errors);
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_Shows_Error_Summary_On_Validation_Failure()
    {
        var svc = SvcWithRules("etag-1");
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Invalid(new[] { "bad rules" }));

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(SaveableForm("etag-1"), default)).Model);

        Assert.Contains("bad rules", vm.Errors);
    }

    [Fact]
    public async Task Save_Blocks_On_Concurrency_Conflict()
    {
        var svc = SvcWithRules("etag-CURRENT");
        var form = SaveableForm("etag-STALE");

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(form, default)).Model);

        Assert.True(vm.ConcurrencyConflict);
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_Maps_Blob_Conflict_To_Friendly_Rerender()
    {
        var svc = SvcWithRules("etag-1");
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns<RulesConfigSaveResult>(_ => throw new RulesConfigConflictException("If-Match failed"));

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(SaveableForm("etag-1"), default)).Model);

        Assert.True(vm.ConcurrencyConflict);
    }

    [Fact]
    public async Task Save_Blocks_When_Outcome_Key_Unknown()
    {
        var svc = SvcWithRules("etag-1");
        var form = SaveableForm("etag-1");
        form.OutcomeKey = "DOES-NOT-EXIST";

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveBranch(form, default)).Model);

        Assert.Contains(vm.Errors, e => e.Contains("no longer exists"));
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_New_Branch_Inserts_Before_Otherwise()
    {
        var svc = SvcWithRules("etag-1");
        RuleSet? captured = null;
        svc.SaveRulesAsync(Arg.Do<RuleSet>(r => captured = r), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(2));

        var form = SaveableForm("etag-1");
        form.IsNew = true;
        form.BranchId = "EAL-NEW";

        await NewController(svc).SaveBranch(form, default);

        var rules = captured!.Outcomes.First(o => o.Key == "EAL").Rules;
        Assert.Equal(3, rules.Count);
        Assert.Equal("EAL-NEW", rules[^2].Id);
        Assert.IsType<Predicate.Otherwise>(rules[^1].When);
    }

    [Fact]
    public async Task ConfirmRemoveBranch_Returns_View_For_Editable_Branch()
    {
        var result = await NewController(SvcWithRules()).ConfirmRemoveBranch("EAL", "EAL-1", default);
        var model = Assert.IsType<BranchViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("EAL-1", model.Id);
    }

    [Fact]
    public async Task ConfirmRemoveBranch_NotFound_For_Otherwise()
    {
        var result = await NewController(SvcWithRules()).ConfirmRemoveBranch("EAL", "EAL-OTHER", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RemoveBranch_Persists_And_Redirects()
    {
        var svc = SvcWithRules("etag-1");
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(3));

        var result = await NewController(svc).RemoveBranch("EAL", "EAL-1", default);

        Assert.IsType<RedirectToActionResult>(result);
        await svc.Received(1).SaveRulesAsync(
            Arg.Is<RuleSet>(r => r.Outcomes.First(o => o.Key == "EAL").Rules.All(b => b.Id != "EAL-1")),
            "etag-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoveBranch_Persists_Reordered_Set()
    {
        var svc = SvcWithRules("etag-1");
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((new RuleSet("v1", DateTimeOffset.UnixEpoch, new[]
        {
            new OutcomeRules("EAL", "EAL", new[]
            {
                new RuleBranch("EAL-1", DecisionStatus.Scrutiny, new Predicate.IsKnownAndCertain("keyStage")),
                new RuleBranch("EAL-2", DecisionStatus.Scrutiny, new Predicate.IsKnownAndCertain("pupilAge")),
                new RuleBranch("EAL-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
            })
        }), "etag-1"));
        svc.SaveRulesAsync(Arg.Any<RuleSet>(), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(4));

        var result = await NewController(svc).MoveBranch("EAL", "EAL-2", "up", default);

        Assert.IsType<RedirectToActionResult>(result);
        await svc.Received(1).SaveRulesAsync(
            Arg.Is<RuleSet>(r => r.Outcomes.First(o => o.Key == "EAL").Rules[0].Id == "EAL-2"),
            "etag-1", Arg.Any<CancellationToken>());
    }

    // --- Lookup row editing tests (M3) ---

    private static Lookups SampleLookups() => new(new Dictionary<string, IReadOnlyList<string>>
    {
        ["GB"] = new[] { "English" }
    });

    private static IRulesConfigService SvcWithLookups(string etag = "L1")
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), etag));
        return svc;
    }

    [Fact]
    public async Task EditLookupRow_Loads_Existing_Languages()
    {
        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(SvcWithLookups()).EditLookupRow("GB", default)).Model);
        Assert.Equal("GB", vm.Form.Code);
        Assert.Contains("English", vm.Form.Languages);
        Assert.False(vm.Form.IsNew);
    }

    [Fact]
    public async Task AddLookupRow_Seeds_New_Form()
    {
        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(SvcWithLookups()).AddLookupRow(default)).Model);
        Assert.True(vm.Form.IsNew);
        Assert.Single(vm.Form.Languages);
    }

    [Fact]
    public async Task SaveLookupRow_Persists_Merged_Map()
    {
        var svc = SvcWithLookups("L1");
        Lookups? captured = null;
        svc.SaveLookupsAsync(Arg.Do<Lookups>(l => captured = l), "L1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(2));

        var form = new LookupRowEditForm
        {
            Code = "FR", IsNew = true, LoadETag = "L1", Action = "save",
            Languages = new List<string> { "French" }
        };

        var result = await NewController(svc).SaveLookupRow(form, default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(captured!.CountryLanguages.ContainsKey("FR"));
        Assert.True(captured.CountryLanguages.ContainsKey("GB"));
    }

    [Fact]
    public async Task SaveLookupRow_Blocks_On_Concurrency_Conflict()
    {
        var svc = SvcWithLookups("L-CURRENT"); // store moved on since load
        var form = new LookupRowEditForm
        {
            Code = "GB", IsNew = false, LoadETag = "L-STALE", Action = "save",
            Languages = new List<string> { "English" }
        };

        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveLookupRow(form, default)).Model);

        Assert.NotEmpty(vm.Errors);
        await svc.DidNotReceive().SaveLookupsAsync(Arg.Any<Lookups>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveLookupRow_Invalid_Shows_Errors()
    {
        var svc = SvcWithLookups("L1");
        var form = new LookupRowEditForm { Code = "", IsNew = true, LoadETag = "L1", Action = "save",
            Languages = new List<string> { "" } };

        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).SaveLookupRow(form, default)).Model);

        Assert.NotEmpty(vm.Errors);
        await svc.DidNotReceive().SaveLookupsAsync(Arg.Any<Lookups>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransformLookupRow_AddLanguage_ReRenders()
    {
        var form = new LookupRowEditForm { Code = "GB", LoadETag = "L1", Action = "addLanguage",
            Languages = new List<string> { "English" } };

        var vm = Assert.IsType<LookupRowEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(SvcWithLookups()).TransformLookupRow(form, default)).Model);

        Assert.Equal(2, vm.Form.Languages.Count);
    }

    [Fact]
    public async Task RemoveLookupRow_Persists_Without_Code()
    {
        var svc = SvcWithLookups("L1");
        Lookups? captured = null;
        svc.SaveLookupsAsync(Arg.Do<Lookups>(l => captured = l), "L1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(3));

        var result = await NewController(svc).RemoveLookupRow("GB", default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(captured!.CountryLanguages.ContainsKey("GB"));
    }
}
