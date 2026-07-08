using System.Reflection;
using System.Text;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
    public void Controller_Gated_By_RulesConfig_Section()
    {
        var gate = typeof(AdminRulesController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal("rules-config", gate!.SectionKey);
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

    [Fact]
    public async Task Outcomes_Returns_List_Model()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag"));

        var result = await NewController(svc).Outcomes(default);

        var model = Assert.IsType<OutcomesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Single(model.Outcomes);
        Assert.Equal("Inclusion", model.Outcomes[0].Key);
    }

    [Fact]
    public async Task Outcomes_Empty_When_Rules_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));

        var result = await NewController(svc).Outcomes(default);

        var model = Assert.IsType<OutcomesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public async Task Outcome_Returns_Detail_For_Known_Key()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag"));

        var result = await NewController(svc).Outcome("Inclusion", default);

        var model = Assert.IsType<OutcomeDetailViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Inclusion", model.Key);
        Assert.Single(model.Branches);
        Assert.Equal("Otherwise (always matches)", model.Branches[0].Condition.Text);
    }

    [Fact]
    public async Task Outcome_Returns_NotFound_For_Unknown_Key()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag"));

        var result = await NewController(svc).Outcome("Nope", default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Outcome_Returns_NotFound_When_Rules_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));

        var result = await NewController(svc).Outcome("Inclusion", default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Lookups_Returns_Rows()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetLookupsAsync(Arg.Any<CancellationToken>()).Returns((SampleLookups(), "etag"));

        var result = await NewController(svc).Lookups(default);

        var model = Assert.IsType<LookupsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Single(model.Rows);
        Assert.Equal("GB", model.Rows[0].CountryCode);
    }

    [Fact]
    public async Task Lookups_Empty_When_Missing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetLookupsAsync(Arg.Any<CancellationToken>())
            .Returns<(Lookups, string?)>(_ => throw new RulesConfigNotFoundException("country-languages.json not found"));

        var result = await NewController(svc).Lookups(default);

        var model = Assert.IsType<LookupsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsEmpty);
    }

    private static RulesConfigVersionDto Version(int id, int num, string content = "{}") => new()
    {
        Id = id, ConfigType = RulesConfigType.Rules, VersionNumber = num,
        CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = "Ada", Content = content
    };

    [Fact]
    public async Task History_Returns_Versions_For_Valid_Type()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.ListVersionsAsync(RulesConfigType.Rules, Arg.Any<CancellationToken>())
            .Returns(new[] { Version(1, 1), Version(2, 2) });

        var result = await NewController(svc).History("Rules", default);

        var model = Assert.IsType<HistoryViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(RulesConfigType.Rules, model.ConfigType);
        Assert.Equal(2, model.Versions.Count);
        Assert.Equal(2, model.Versions[0].VersionNumber); // newest first
    }

    [Fact]
    public async Task History_NotFound_For_Invalid_Type()
    {
        var svc = Substitute.For<IRulesConfigService>();
        var result = await NewController(svc).History("Bananas", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Version_Returns_Detail_For_Known_Id()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.ListVersionsAsync(RulesConfigType.Rules, Arg.Any<CancellationToken>())
            .Returns(new[] { Version(7, 3, "{\"x\":1}") });

        var result = await NewController(svc).Version("Rules", 7, default);

        var model = Assert.IsType<VersionDetailViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(3, model.VersionNumber);
        Assert.Equal("{\"x\":1}", model.Content);
    }

    [Fact]
    public async Task Version_NotFound_For_Unknown_Id()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.ListVersionsAsync(RulesConfigType.Rules, Arg.Any<CancellationToken>())
            .Returns(new[] { Version(7, 3) });

        var result = await NewController(svc).Version("Rules", 999, default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Version_NotFound_For_Invalid_Type()
    {
        var svc = Substitute.For<IRulesConfigService>();
        var result = await NewController(svc).Version("Bananas", 1, default);
        Assert.IsType<NotFoundResult>(result);
    }

    // --- Upload ---

    private static IFormFile FakeFile(string content)
    {
        var file = Substitute.For<IFormFile>();
        var bytes = Encoding.UTF8.GetBytes(content);
        file.Length.Returns(bytes.LongLength);
        file.FileName.Returns("rules.json");
        file.OpenReadStream().Returns(_ => new MemoryStream(bytes));
        return file;
    }

    private static AdminRulesController NewControllerWithTempData(IRulesConfigService svc)
    {
        var controller = NewController(svc);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task Upload_Get_NotFound_For_Invalid_Type()
    {
        var svc = Substitute.For<IRulesConfigService>();
        var result = await NewController(svc).Upload("Bananas", default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Upload_Get_Returns_View_When_Config_Empty()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));

        var result = await NewController(svc).Upload("Rules", default);

        var model = Assert.IsType<RulesUploadViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(RulesConfigType.Rules, model.Type);
        Assert.Empty(model.Errors);
    }

    [Fact]
    public async Task Upload_Get_Redirects_When_Config_Has_Data()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag-r"));

        var result = await NewController(svc).Upload("Rules", default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminRulesController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task Upload_Post_No_File_Returns_View_With_Error()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));

        var result = await NewController(svc).Upload("Rules", file: null, default);

        var model = Assert.IsType<RulesUploadViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Contains("Select a file to upload", model.Errors);
        await svc.DidNotReceive().ImportRulesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_Post_When_Config_Has_Data_Redirects_Without_Importing()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>()).Returns((SampleRules(), "etag-r"));

        var result = await NewController(svc).Upload("Rules", FakeFile("{}"), default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminRulesController.Index), redirect.ActionName);
        await svc.DidNotReceive().ImportRulesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_Post_Valid_Imports_And_Redirects_With_Success()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetRulesAsync(Arg.Any<CancellationToken>())
            .Returns<(RuleSet, string?)>(_ => throw new RulesConfigNotFoundException("rules.json not found"));
        svc.ImportRulesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Success(1));

        var controller = NewControllerWithTempData(svc);
        var result = await controller.Upload("Rules", FakeFile("{\"version\":\"v1\"}"), default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminRulesController.Index), redirect.ActionName);
        Assert.Contains("uploaded", (string)controller.TempData["SuccessMessage"]!);
        await svc.Received(1).ImportRulesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_Post_Import_Errors_Re_Render_View()
    {
        var svc = Substitute.For<IRulesConfigService>();
        svc.GetLookupsAsync(Arg.Any<CancellationToken>())
            .Returns<(Lookups, string?)>(_ => throw new RulesConfigNotFoundException("country-languages.json not found"));
        svc.ImportLookupsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(RulesConfigSaveResult.Invalid(new[] { "The file contains no country-language entries." }));

        var result = await NewController(svc).Upload("Lookups", FakeFile("{}"), default);

        var model = Assert.IsType<RulesUploadViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(RulesConfigType.Lookups, model.Type);
        Assert.NotEmpty(model.Errors);
    }
}
