using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class AdminRulesControllerM4Tests
{
    private static RuleSet Rules() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("Inclusion", "Inclusion", new[]
        {
            new RuleBranch("INC-1", DecisionStatus.Scrutiny, new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))),
            new RuleBranch("INC-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
        }),
        new OutcomeRules("AdminAdded", "Admin added", new[]
        {
            new RuleBranch("AdminAdded-OTHER", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance),
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
        var c = new AdminRulesController(svc);
        c.TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>());
        return c;
    }

    [Fact]
    public void AddOutcomeForm_Returns_Empty_Form()
    {
        var vm = Assert.IsType<AddOutcomeViewModel>(
            Assert.IsType<ViewResult>(NewController(SvcWithRules()).AddOutcomeForm()).Model);
        Assert.Equal(string.Empty, vm.Form.Key);
    }

    [Fact]
    public async Task AddOutcome_Valid_Persists_Skeleton_And_Redirects()
    {
        var svc = SvcWithRules("etag-1");
        RuleSet? captured = null;
        svc.SaveRulesAsync(Arg.Do<RuleSet>(r => captured = r), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(2));

        var result = await NewController(svc).AddOutcome(new AddOutcomeForm { Key = "NewOutcome", Label = "New outcome" }, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Outcome", redirect.ActionName);
        var added = captured!.Outcomes.Single(o => o.Key == "NewOutcome");
        Assert.Equal("New outcome", added.Label);
        Assert.Single(added.Rules);
        Assert.IsType<Predicate.Otherwise>(added.Rules[0].When);
        Assert.Equal("NewOutcome-OTHER", added.Rules[0].Id);
    }

    [Fact]
    public async Task AddOutcome_Bad_Key_Shows_Errors_Without_Saving()
    {
        var svc = SvcWithRules();
        var vm = Assert.IsType<AddOutcomeViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).AddOutcome(new AddOutcomeForm { Key = "bad key" }, default)).Model);
        Assert.NotEmpty(vm.Errors);
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddOutcome_Duplicate_Key_Shows_Errors_Without_Saving()
    {
        var svc = SvcWithRules();
        var vm = Assert.IsType<AddOutcomeViewModel>(
            Assert.IsType<ViewResult>(await NewController(svc).AddOutcome(new AddOutcomeForm { Key = "Inclusion" }, default)).Model);
        Assert.Contains(vm.Errors, e => e.Contains("already exists"));
        await svc.DidNotReceive().SaveRulesAsync(Arg.Any<RuleSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddOutcome_Defaults_Label_To_Key_When_Blank()
    {
        var svc = SvcWithRules("etag-1");
        RuleSet? captured = null;
        svc.SaveRulesAsync(Arg.Do<RuleSet>(r => captured = r), "etag-1", Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(2));

        await NewController(svc).AddOutcome(new AddOutcomeForm { Key = "Blankish", Label = "" }, default);

        Assert.Equal("Blankish", captured!.Outcomes.Single(o => o.Key == "Blankish").Label);
    }
}
