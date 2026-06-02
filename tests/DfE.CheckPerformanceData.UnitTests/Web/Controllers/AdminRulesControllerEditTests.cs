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
}
