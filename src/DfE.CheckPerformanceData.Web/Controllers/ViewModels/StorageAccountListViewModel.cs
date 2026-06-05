namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class StorageAccountListViewModel
{
    public IReadOnlyList<StorageAccountViewModel> Accounts { get; init; } = [];
}

public sealed class StorageAccountViewModel
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
