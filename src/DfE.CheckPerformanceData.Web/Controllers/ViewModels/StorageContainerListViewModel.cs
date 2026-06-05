namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class StorageContainerListViewModel
{
    public string AccountKey { get; init; } = string.Empty;
    public string AccountDisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Containers { get; init; } = [];
}
