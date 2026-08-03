using DfE.CheckPerformanceData.Application.Settings;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class AdminSettingsViewModel
{
    public required IReadOnlyList<SettingViewItem> Settings { get; init; }

    public required SettingSortDirection SortDirection { get; init; }

    // The value the sort-toggle anchor's href should carry so a click flips the direction.
    // Used as the ?sort= query string parameter on the column-header link.
    public string ReverseSortQuery =>
        SortDirection == SettingSortDirection.KeyAscending ? "key-desc" : "key-asc";
}
