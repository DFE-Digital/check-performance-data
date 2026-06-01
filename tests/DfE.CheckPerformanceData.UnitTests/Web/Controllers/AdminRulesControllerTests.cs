using System.Reflection;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class AdminRulesControllerTests
{
    private static RuleSet SampleRules() => new(
        "v1", DateTimeOffset.UnixEpoch,
        new[]
        {
            new OutcomeRules("Inclusion", "Inclusion",
                new[] { new RuleBranch("INC-DEF", DecisionStatus.Scrutiny, Predicate.Otherwise.Instance) })
        });

    private static Lookups SampleLookups() => new(new Dictionary<string, IReadOnlyList<string>>
    {
        ["GB"] = new[] { "English" }
    });

    private static AdminRulesController NewController(IRulesConfigService svc) => new(svc);

    [Fact]
    public void Controller_Has_Authorize_AdminRole()
    {
        var authorize = typeof(AdminRulesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public void Index_Has_HttpGet_AdminRules_Route()
    {
        var method = typeof(AdminRulesController).GetMethod("Index");
        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("admin/rules", httpGet!.Template);
    }

    [Fact]
    public async Task Index_Returns_Landing_With_Both_Cards_Populated()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag-r"));
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), "etag-l"));
        svc.ListVersionsAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RulesConfigVersionDto>());

        var result = await NewController(svc).Index(default);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RulesLandingViewModel>(view.Model);
        Assert.False(model.Rules.IsEmpty);
        Assert.Equal(1, model.Rules.ItemCount);
        Assert.False(model.Lookups.IsEmpty);
        Assert.Equal(1, model.Lookups.ItemCount);
    }

    [Fact]
    public async Task Index_Renders_Empty_Card_When_Rules_Blob_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), "etag-l"));
        svc.ListVersionsAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RulesConfigVersionDto>());

        var result = await NewController(svc).Index(default);

        var model = Assert.IsType<RulesLandingViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.Rules.IsEmpty);
        Assert.False(model.Lookups.IsEmpty); // lookups still rendered
    }
}
