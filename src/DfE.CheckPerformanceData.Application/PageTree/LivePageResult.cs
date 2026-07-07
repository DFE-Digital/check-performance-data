namespace DfE.CheckPerformanceData.Application.PageTree;

// Returned by IPageNodeService.GetLivePageAsync when a node has a version live at nowUtc.
// Null is returned from the service when the path is unknown or no version is currently live.
public sealed class LivePageResult
{
    public required PageNodeDto Node { get; init; }
    public required PageNodeVersionDto Version { get; init; }
}
