using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.SiteAssets;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.SiteAssets;

public sealed class SiteAssetServiceTests
{
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly SiteAssetService _sut;

    public SiteAssetServiceTests()
    {
        _sut = new SiteAssetService(_settings, _cache);
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("");
        _settings.GetValueAsync(SettingKeys.SiteJs).Returns("");
        _settings.GetBoolAsync(SettingKeys.SiteCssEnabled).Returns(true);
        _settings.GetBoolAsync(SettingKeys.SiteJsEnabled).Returns(true);
    }

    [Fact]
    public async Task Get_NothingStored_ServesNeither()
    {
        var content = await _sut.GetAsync();

        Assert.False(content.ServesCss);
        Assert.False(content.ServesJs);
    }

    [Fact]
    public async Task Get_StoredCssAndJs_AreServed()
    {
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("h1{color:red}");
        _settings.GetValueAsync(SettingKeys.SiteJs).Returns("console.log(1)");

        var content = await _sut.GetAsync();

        Assert.True(content.ServesCss);
        Assert.True(content.ServesJs);
        Assert.Equal("h1{color:red}", content.Css);
        Assert.Equal("console.log(1)", content.Js);
    }

    [Fact]
    public async Task Get_SwitchedOff_IsNotServedEvenWhenContentIsStored()
    {
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("h1{color:red}");
        _settings.GetValueAsync(SettingKeys.SiteJs).Returns("console.log(1)");
        _settings.GetBoolAsync(SettingKeys.SiteCssEnabled).Returns(false);
        _settings.GetBoolAsync(SettingKeys.SiteJsEnabled).Returns(false);

        var content = await _sut.GetAsync();

        Assert.False(content.ServesCss);
        Assert.False(content.ServesJs);
    }

    [Fact]
    public void Version_ChangesWithContent_AndIsStableOtherwise()
    {
        var first = new SiteAssetContent("a{}", "x", true, true);
        var same = new SiteAssetContent("a{}", "y", false, false);
        var changed = new SiteAssetContent("b{}", "x", true, true);

        Assert.Equal(first.CssVersion, same.CssVersion);
        Assert.NotEqual(first.CssVersion, changed.CssVersion);
        Assert.NotEqual(first.JsVersion, same.JsVersion);
        Assert.Matches("^[0-9a-f]{12}$", first.CssVersion);
    }

    [Fact]
    public async Task Get_IsCached_SoRepeatReadsDoNotHitTheStore()
    {
        await _sut.GetAsync();
        await _sut.GetAsync();

        await _settings.Received(1).GetValueAsync(SettingKeys.SiteCss);
    }

    [Fact]
    public async Task Save_PersistsAllFourSettings_AndDropsTheCache()
    {
        await _sut.GetAsync();

        var result = await _sut.SaveAsync(new SiteAssetContent("h1{}", "var a=1;", true, false));

        Assert.True(result.Succeeded);
        await _settings.Received(1).SaveAsync(SettingKeys.SiteCss, "h1{}");
        await _settings.Received(1).SaveAsync(SettingKeys.SiteJs, "var a=1;");
        await _settings.Received(1).SaveAsync(SettingKeys.SiteCssEnabled, "true");
        await _settings.Received(1).SaveAsync(SettingKeys.SiteJsEnabled, "false");

        await _sut.GetAsync();
        await _settings.Received(2).GetValueAsync(SettingKeys.SiteCss);
    }

    [Fact]
    public async Task Save_OverTheSizeLimit_IsRejectedAndNothingIsWritten()
    {
        var tooBig = new string('a', SiteAssetService.MaxLength + 1);

        var result = await _sut.SaveAsync(new SiteAssetContent(tooBig, "", true, true));

        Assert.False(result.Succeeded);
        Assert.Contains("CSS", result.Error);
        await _settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public void Managed_Settings_AreDeclared_ButNotOfferedOnTheGeneralSettingsPage()
    {
        foreach (var key in new[] { SettingKeys.SiteCss, SettingKeys.SiteJs, SettingKeys.SiteCssEnabled, SettingKeys.SiteJsEnabled })
        {
            var definition = SettingDefinitions.Find(key);
            Assert.NotNull(definition);
            Assert.True(definition!.ManagedElsewhere);
        }
    }

    [Fact]
    public async Task GeneralSettingsList_OmitsManagedSettings()
    {
        var repository = Substitute.For<ISettingRepository>();
        repository.GetAllAsync().Returns(new Dictionary<string, string>());
        var service = new SettingService(repository);

        var all = await service.GetAllWithValuesAsync();

        Assert.DoesNotContain(all, s => s.Key.StartsWith("SiteAssets:"));
        Assert.Contains(all, s => s.Key == SettingKeys.CmsPageLength);
    }
}
