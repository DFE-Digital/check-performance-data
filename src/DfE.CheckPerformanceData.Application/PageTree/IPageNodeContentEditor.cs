using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Draft widget-tree mutation operations for a PageNode's working version. Keyed by node Guid,
// mirrors IContentPageEditor but loads/saves via IPageNodeService rather than IContentPageRepository.
// No version is created here — that happens only when the page is published.
public interface IPageNodeContentEditor
{
    Task AddWidgetAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string type, string? userId);

    Task AddRegionAsync(Guid nodeId, IReadOnlyList<TreeStep> path, RegionLayout layout, string? userId);

    Task MoveUpAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string? userId);

    Task MoveDownAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string? userId);

    Task DeleteAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string? userId);

    Task UpdateWidgetAsync(Guid nodeId, IReadOnlyList<TreeStep> path, JsonObject props, string? userId);

    Task MoveToAsync(Guid nodeId, IReadOnlyList<TreeStep> fromPath, IReadOnlyList<TreeStep> toPath, string? userId);
}
