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
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 30, 15, 30, 12, TimeSpan.Zero));

    public SiteAssetServiceTests()
    {
        _sut = new SiteAssetService(_settings, _cache, _clock);
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("");
        _settings.GetValueAsync(SettingKeys.SiteJs).Returns("");
        _settings.GetBoolAsync(SettingKeys.SiteCssEnabled).Returns(true);
        _settings.GetBoolAsync(SettingKeys.SiteJsEnabled).Returns(true);
        _settings.GetValueAsync(SettingKeys.SiteCssSavedAt).Returns("");
        _settings.GetValueAsync(SettingKeys.SiteJsSavedAt).Returns("");
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
    public async Task Get_Version_IsTheStoredSaveTimestamp_PerAsset()
    {
        _settings.GetValueAsync(SettingKeys.SiteCssSavedAt).Returns("20260930153012");
        _settings.GetValueAsync(SettingKeys.SiteJsSavedAt).Returns("20260101000000");

        var content = await _sut.GetAsync();

        Assert.Equal("20260930153012", content.CssVersion);
        Assert.Equal("20260101000000", content.JsVersion);
    }

    [Fact]
    public async Task Get_NeverSaved_HasNoVersion()
    {
        var content = await _sut.GetAsync();

        Assert.Equal("", content.CssVersion);
        Assert.Equal("", content.JsVersion);
    }

    [Fact]
    public async Task Save_StampsUtcTimestampInTheAgreedFormat_ForAnAssetThatChanged()
    {
        await _sut.SaveAsync(new SiteAssetContent("h1{}", "", true, true));

        Assert.Equal("20260930153012", Saved()[SettingKeys.SiteCssSavedAt]);
    }

    [Fact]
    public async Task Save_DoesNotRestampAnAssetThatWasNotChanged()
    {
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("h1{}");
        _settings.GetValueAsync(SettingKeys.SiteCssSavedAt).Returns("20200101000000");

        await _sut.SaveAsync(new SiteAssetContent("h1{}", "var a=1;", true, true));

        Assert.False(Saved().ContainsKey(SettingKeys.SiteCssSavedAt));
        Assert.Equal("20260930153012", Saved()[SettingKeys.SiteJsSavedAt]);
    }

    // The store trims what it saves, so a textarea's trailing newline is not a change.
    [Fact]
    public async Task Save_SameContentWithSurroundingWhitespace_IsNotAChange()
    {
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("body{}");
        _settings.GetValueAsync(SettingKeys.SiteCssSavedAt).Returns("20200101000000");

        await _sut.SaveAsync(new SiteAssetContent("body{}\r\n", "  \n", true, true));

        var saved = Saved();
        Assert.False(saved.ContainsKey(SettingKeys.SiteCssSavedAt));
        Assert.False(saved.ContainsKey(SettingKeys.SiteJsSavedAt));
        Assert.Equal("body{}", saved[SettingKeys.SiteCss]);
        Assert.Equal("", saved[SettingKeys.SiteJs]);
    }

    [Fact]
    public async Task Save_RealChangeWithATrailingNewline_IsRestamped()
    {
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("body{}");
        _settings.GetValueAsync(SettingKeys.SiteCssSavedAt).Returns("20200101000000");

        await _sut.SaveAsync(new SiteAssetContent("body{color:red}\n", "", true, true));

        Assert.Equal("20260930153012", Saved()[SettingKeys.SiteCssSavedAt]);
        Assert.Equal("body{color:red}", Saved()[SettingKeys.SiteCss]);
    }

    [Fact]
    public async Task Save_TogglingAnAssetOff_RestampsIt()
    {
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("h1{}");

        await _sut.SaveAsync(new SiteAssetContent("h1{}", "", false, true));

        Assert.Equal("20260930153012", Saved()[SettingKeys.SiteCssSavedAt]);
    }

    [Fact]
    public async Task NextRead_AfterASave_SeesTheNewContentAndVersion()
    {
        await _sut.GetAsync();
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("h1{}");
        _settings.GetValueAsync(SettingKeys.SiteCssSavedAt).Returns("20260930153012");

        await _sut.SaveAsync(new SiteAssetContent("h1{}", "", true, true));
        var next = await _sut.GetAsync();

        Assert.Equal("h1{}", next.Css);
        Assert.Equal("20260930153012", next.CssVersion);
    }

    // Another server may have saved since this one cached the assets. Saving the content this
    // server last saw must still restamp, or browsers holding the other save's URL keep its content.
    [Fact]
    public async Task Save_DecidesWhatChangedFromTheStore_NotFromTheCache()
    {
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("h1{}");
        await _sut.GetAsync();
        _settings.GetValueAsync(SettingKeys.SiteCss).Returns("p{}");
        _settings.GetValueAsync(SettingKeys.SiteCssSavedAt).Returns("20260101000000");

        await _sut.SaveAsync(new SiteAssetContent("h1{}", "", true, true));

        Assert.Equal("20260930153012", Saved()[SettingKeys.SiteCssSavedAt]);
    }

    [Fact]
    public async Task Get_IsCached_SoRepeatReadsDoNotHitTheStore()
    {
        await _sut.GetAsync();
        await _sut.GetAsync();

        await _settings.Received(1).GetValueAsync(SettingKeys.SiteCss);
    }

    [Fact]
    public async Task Save_PersistsTheContentAndSwitches_AndDropsTheCache()
    {
        await _sut.GetAsync();

        var result = await _sut.SaveAsync(new SiteAssetContent("h1{}", "var a=1;", true, false));

        Assert.True(result.Succeeded);
        var saved = Saved();
        Assert.Equal("h1{}", saved[SettingKeys.SiteCss]);
        Assert.Equal("var a=1;", saved[SettingKeys.SiteJs]);
        Assert.Equal("true", saved[SettingKeys.SiteCssEnabled]);
        Assert.Equal("false", saved[SettingKeys.SiteJsEnabled]);
        await _settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);

        _settings.ClearReceivedCalls();
        await _sut.GetAsync();
        await _settings.Received(1).GetValueAsync(SettingKeys.SiteCss);
    }

    [Fact]
    public async Task Save_OverTheSizeLimit_IsRejectedAndNothingIsWritten()
    {
        var tooBig = new string('a', SiteAssetService.MaxLength + 1);

        var result = await _sut.SaveAsync(new SiteAssetContent(tooBig, "", true, true));

        Assert.False(result.Succeeded);
        Assert.Contains("CSS", result.Error);
        await _settings.DidNotReceiveWithAnyArgs().SaveManyAsync(default!);
    }

    // Content, switches and versions go to the store as one write. If part of it fails, nothing is
    // left half-saved and the editor is told, rather than the page failing.
    [Fact]
    public async Task Save_WhenTheStoreFails_ReportsItAndDoesNotThrow()
    {
        _settings.SaveManyAsync(Arg.Any<IReadOnlyDictionary<string, string?>>())
            .Returns(Task.FromException(new InvalidOperationException("database unavailable")));

        var result = await _sut.SaveAsync(new SiteAssetContent("h1{}", "", true, true));

        Assert.False(result.Succeeded);
        Assert.Contains("could not be saved", result.Error);
    }

    [Fact]
    public async Task Save_WhenTheStoreFails_TheNextReadGoesBackToTheStore()
    {
        await _sut.GetAsync();
        _settings.SaveManyAsync(Arg.Any<IReadOnlyDictionary<string, string?>>())
            .Returns(Task.FromException(new InvalidOperationException("database unavailable")));

        await _sut.SaveAsync(new SiteAssetContent("h1{}", "", true, true));
        _settings.ClearReceivedCalls();
        await _sut.GetAsync();

        await _settings.Received(1).GetValueAsync(SettingKeys.SiteCss);
    }

    [Fact]
    public void Managed_Settings_AreDeclared_ButNotOfferedOnTheGeneralSettingsPage()
    {
        foreach (var key in new[] { SettingKeys.SiteCss, SettingKeys.SiteJs, SettingKeys.SiteCssEnabled, SettingKeys.SiteJsEnabled, SettingKeys.SiteCssSavedAt, SettingKeys.SiteJsSavedAt })
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

    // The single batch of settings the last save handed to the store.
    private IReadOnlyDictionary<string, string?> Saved()
    {
        var call = Assert.Single(_settings.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(ISettingService.SaveManyAsync));
        return (IReadOnlyDictionary<string, string?>)call.GetArguments()[0]!;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
