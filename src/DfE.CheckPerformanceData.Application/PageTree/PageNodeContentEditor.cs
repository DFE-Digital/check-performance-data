using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Applies a single editor action to a PageNode's working draft version: loads the draft's content
// tree via IPageNodeService, mutates it through the pure ContentTreeEditor, re-anchors heading
// widgets, derives a plain-text body for FTS indexing, and autosaves via SaveWorkingContentAsync.
// No version is created here — that happens only when the page is published.
public sealed class PageNodeContentEditor(
    IPageNodeService pageNodes,
    IHtmlRenderingService htmlRenderer) : IPageNodeContentEditor
{
    public Task AddWidgetAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string type, string? userId) =>
        MutateAsync(nodeId, userId, tree => ContentTreeEditor.InsertAt(tree, path, new WidgetNode
        {
            Type = type,
            Props = WidgetRegistry.CreateDefaultProps(type)
        }));

    public Task AddRegionAsync(Guid nodeId, IReadOnlyList<TreeStep> path, RegionLayout layout, string? userId) =>
        MutateAsync(nodeId, userId, tree => ContentTreeEditor.InsertAt(tree, path, new RegionNode
        {
            Layout = layout,
            Columns = EmptyColumns(layout)
        }));

    public Task MoveUpAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string? userId) =>
        MutateAsync(nodeId, userId, tree => ContentTreeEditor.MoveUp(tree, path));

    public Task MoveDownAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string? userId) =>
        MutateAsync(nodeId, userId, tree => ContentTreeEditor.MoveDown(tree, path));

    public Task DeleteAsync(Guid nodeId, IReadOnlyList<TreeStep> path, string? userId) =>
        MutateAsync(nodeId, userId, tree => ContentTreeEditor.Remove(tree, path));

    public Task UpdateWidgetAsync(Guid nodeId, IReadOnlyList<TreeStep> path, JsonObject props, string? userId) =>
        MutateAsync(nodeId, userId, tree =>
        {
            if (ContentTreeEditor.NodeAt(tree, path) is WidgetNode widget)
                widget.Props = props;
        });

    public Task MoveToAsync(Guid nodeId, IReadOnlyList<TreeStep> fromPath, IReadOnlyList<TreeStep> toPath, string? userId) =>
        MutateAsync(nodeId, userId, tree => ContentTreeEditor.MoveTo(tree, fromPath, toPath));

    private async Task MutateAsync(Guid nodeId, string? userId, Action<List<ContentNode>> edit)
    {
        // Load draft if one exists, else the latest published version's content.
        // Avoids editing a blank document after a page has been published (no draft remains).
        var json = await pageNodes.GetWorkingOrLatestContentAsync(nodeId) ?? "[]";

        var tree = (ContentPageJson.Deserialize(json) ?? []).ToList();
        edit(tree);
        HeadingAnchorizer.Apply(tree);

        var newJson = ContentPageJson.Serialize(tree);
        var bodyPlainText = ExtractPlainText(tree);

        await pageNodes.SaveWorkingContentAsync(nodeId, newJson, bodyPlainText, userId);
    }

    // Derives a searchable plain-text body from the widget tree for FTS indexing.
    // No content-page → plain-text helper existed in the codebase; IHtmlRenderingService.StripTagsToPlainText
    // is reused for richtext HTML. Concatenates heading text, stripped richtext HTML, card title+body,
    // and published callout text. Dividers and summary-lists contribute nothing (no prose content).
    private string ExtractPlainText(IReadOnlyList<ContentNode> tree)
    {
        var parts = new List<string>();

        foreach (var widget in ContentTreeWalker.AllWidgets(tree))
        {
            var text = widget.Type switch
            {
                "heading"   => widget.GetString("text"),
                "richtext"  => htmlRenderer.StripTagsToPlainText(widget.GetString("html")),
                "card"      => JoinNonEmpty(widget.GetString("title"), widget.GetString("body")),
                "published" => widget.GetString("text"),
                _           => null
            };

            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        return string.Join(" ", parts);
    }

    private static string? JoinNonEmpty(params string?[] values)
    {
        var joined = string.Join(" ", values.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static List<List<ContentNode>> EmptyColumns(RegionLayout layout) =>
        Enumerable.Range(0, RegionLayoutGrid.ColumnCount(layout))
            .Select(_ => new List<ContentNode>())
            .ToList();
}
