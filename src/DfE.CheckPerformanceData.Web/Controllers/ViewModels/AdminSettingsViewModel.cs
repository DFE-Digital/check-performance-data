using DfE.CheckPerformanceData.Application.Settings;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class AdminSettingsViewModel
{
    public required IReadOnlyList<SettingViewItem> Settings { get; init; }
}
