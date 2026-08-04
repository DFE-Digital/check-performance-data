using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Application.Settings;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Contract tests for CmsSettingsSearchDebugOptions — the ISearchDebugOptions concrete
// backed by ISettingService (reads CMS:SearchDebugOn).
//
// Two pinned behaviours:
//   1. ShowSearchDebug returns whatever the settings service says for CMS:SearchDebugOn.
//   2. The blocking async read is cached (Lazy<bool>) — repeated reads within one
//      resolved instance hit the settings service exactly once. This bounds the
//      DB-round-trip cost of the sync accessor to at most one per request scope
//      (the concrete is Scoped-registered).
public sealed class CmsSettingsSearchDebugOptionsTests
{
    private readonly ISettingService _settings = Substitute.For<ISettingService>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShowSearchDebug_ReturnsWhateverSettingsServiceReports(bool stored)
    {
        _settings.GetBoolAsync(SettingKeys.SearchDebugOn).Returns(Task.FromResult(stored));

        var sut = new CmsSettingsSearchDebugOptions(_settings);

        Assert.Equal(stored, sut.ShowSearchDebug);
    }

    [Fact]
    public async Task ShowSearchDebug_CachesFirstRead_AndDoesNotHitSettingsServiceAgain()
    {
        _settings.GetBoolAsync(SettingKeys.SearchDebugOn).Returns(Task.FromResult(true));

        var sut = new CmsSettingsSearchDebugOptions(_settings);

        _ = sut.ShowSearchDebug;
        _ = sut.ShowSearchDebug;
        _ = sut.ShowSearchDebug;

        await _settings.Received(1).GetBoolAsync(SettingKeys.SearchDebugOn);
    }
}
