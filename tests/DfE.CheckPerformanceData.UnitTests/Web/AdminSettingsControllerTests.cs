using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Web;

public sealed class AdminSettingsControllerTests
{
    private readonly ISettingService _settings = Substitute.For<ISettingService>();
    private readonly AdminSettingsController _sut;

    public AdminSettingsControllerTests()
    {
        _sut = new AdminSettingsController(_settings)
        {
            TempData = new TempDataDictionary(
                new DefaultHttpContext(), Substitute.For<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task Save_BoolSetting_Checked_PersistsTrue()
    {
        await _sut.Save(SettingKeys.DlqFullPayloadEnabled, value: null, boolValue: "true");

        await _settings.Received(1).SaveAsync(SettingKeys.DlqFullPayloadEnabled, "true");
    }

    [Fact]
    public async Task Save_BoolSetting_Unchecked_PersistsFalse()
    {
        // Unchecked checkboxes are not posted at all, so boolValue arrives null.
        await _sut.Save(SettingKeys.DlqFullPayloadEnabled, value: null, boolValue: null);

        await _settings.Received(1).SaveAsync(SettingKeys.DlqFullPayloadEnabled, "false");
    }

    [Fact]
    public async Task Save_NonBoolSetting_UsesTextValue()
    {
        await _sut.Save(SettingKeys.CmsPageLength, value: "40", boolValue: null);

        await _settings.Received(1).SaveAsync(SettingKeys.CmsPageLength, "40");
    }
}
