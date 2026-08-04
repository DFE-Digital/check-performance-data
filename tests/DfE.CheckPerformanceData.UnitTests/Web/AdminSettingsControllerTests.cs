using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
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

    [Fact]
    public async Task Index_NoSortParam_ReturnsViewModelWithSettingsInKeyAscendingOrder()
    {
        StubSettings();

        var vm = await GetIndexViewModel(sort: null);

        Assert.Equal(SettingSortDirection.KeyAscending, vm.SortDirection);
        Assert.Equal(
            new[] { "Alpha:One", "Bravo:Two", "Charlie:Three" },
            vm.Settings.Select(s => s.Key));
    }

    [Fact]
    public async Task Index_SortKeyDesc_ReturnsViewModelWithSettingsInKeyDescendingOrder()
    {
        StubSettings();

        var vm = await GetIndexViewModel(sort: "key-desc");

        Assert.Equal(SettingSortDirection.KeyDescending, vm.SortDirection);
        Assert.Equal(
            new[] { "Charlie:Three", "Bravo:Two", "Alpha:One" },
            vm.Settings.Select(s => s.Key));
    }

    [Fact]
    public async Task Index_SortKeyAsc_ReturnsViewModelWithSettingsInKeyAscendingOrder()
    {
        StubSettings();

        var vm = await GetIndexViewModel(sort: "key-asc");

        Assert.Equal(SettingSortDirection.KeyAscending, vm.SortDirection);
        Assert.Equal(
            new[] { "Alpha:One", "Bravo:Two", "Charlie:Three" },
            vm.Settings.Select(s => s.Key));
    }

    [Fact]
    public async Task Index_SortUnrecognised_FallsBackToKeyAscending()
    {
        StubSettings();

        var vm = await GetIndexViewModel(sort: "not-a-real-value");

        Assert.Equal(SettingSortDirection.KeyAscending, vm.SortDirection);
        Assert.Equal(
            new[] { "Alpha:One", "Bravo:Two", "Charlie:Three" },
            vm.Settings.Select(s => s.Key));
    }

    [Fact]
    public async Task Index_SortNull_UsesKeyAscending()
    {
        StubSettings();

        var vm = await GetIndexViewModel(sort: null);

        Assert.Equal(SettingSortDirection.KeyAscending, vm.SortDirection);
    }

    private void StubSettings()
    {
        // Deliberately unsorted so the assertion catches a controller that forwards raw order.
        _settings.GetAllWithValuesAsync().Returns(new List<SettingViewItem>
        {
            new("Charlie:Three", "third", "c", "c", true),
            new("Alpha:One", "first", "a", "a", true),
            new("Bravo:Two", "second", "b", "b", true),
        });
    }

    private async Task<AdminSettingsViewModel> GetIndexViewModel(string? sort)
    {
        var result = await _sut.Index(sort);
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<AdminSettingsViewModel>(view.Model);
    }
}
