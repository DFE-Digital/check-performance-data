namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class StorageContainerListViewModel
{
    public IReadOnlyList<string> Containers { get; init; } = [];
}
