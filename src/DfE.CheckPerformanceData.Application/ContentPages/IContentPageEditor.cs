using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// Draft-mutation operations behind the inline editor. Each addresses a position with a tree path and
// autosaves the draft (no version is created until the page is published).
public interface IContentPageEditor
{
    Task AddWidgetAsync(string slug, IReadOnlyList<TreeStep> path, string type);

    Task AddRegionAsync(string slug, IReadOnlyList<TreeStep> path, RegionLayout layout);

    Task MoveUpAsync(string slug, IReadOnlyList<TreeStep> path);

    Task MoveDownAsync(string slug, IReadOnlyList<TreeStep> path);

    Task DeleteAsync(string slug, IReadOnlyList<TreeStep> path);

    Task UpdateWidgetAsync(string slug, IReadOnlyList<TreeStep> path, JsonObject props);
}
