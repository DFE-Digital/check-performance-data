using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
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

    private static AdminRulesController NewController(IRulesConfigService svc) => new(svc);

    [Fact]
    public async Task EditBranch_Loads_Form_With_Captured_ETag()
    {
        var result = await NewController(SvcWithRules("etag-xyz")).EditBranch("EAL", "EAL-1", default);

        var vm = Assert.IsType<BranchEditViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("EAL-1", vm.Form.BranchId);
        Assert.False(vm.Form.IsNew);
        Assert.Equal("etag-xyz", vm.Form.LoadETag);
        Assert.NotEmpty(vm.Form.Nodes);
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
    public async Task Transform_GroupSelected_Builds_New_Composite()
    {
        var svc = SvcWithRules();
        var form = new BranchEditForm
        {
            OutcomeKey = "EAL", BranchId = "EAL-1", LoadETag = "etag-1", Action = "group:any",
            Nodes = new List<PredicateNodeForm>
            {
                new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
                new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "A", Selected = true },
                new() { Id = 3, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "keyStage", Operator = "eq", Value = "B", Selected = true },
            }
        };

        var vm = Assert.IsType<BranchEditViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).TransformBranch(form, default)).Model);

        Assert.Contains(vm.Form.Nodes, n => n.Kind == PredicateKind.AnyOf && n.ParentId == 1);
    }
}
