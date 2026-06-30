using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// Applies a single editor action to a page's draft: load the draft tree, mutate it through the pure
// ContentTreeEditor, re-anchor headings, and autosave. New widgets/regions start from the registry's
// defaults (and a region gets one empty column per its layout). No version is created here — that
// happens only when the page is published.
public sealed class ContentPageEditor(IContentPageRepository repository) : IContentPageEditor
{
    public Task AddWidgetAsync(string slug, IReadOnlyList<TreeStep> path, string type) =>
        MutateAsync(slug, tree => ContentTreeEditor.InsertAt(tree, path, new WidgetNode
        {
            Type = type,
            Props = WidgetRegistry.CreateDefaultProps(type)
        }));

    public Task AddRegionAsync(string slug, IReadOnlyList<TreeStep> path, RegionLayout layout) =>
        MutateAsync(slug, tree => ContentTreeEditor.InsertAt(tree, path, new RegionNode
        {
            Layout = layout,
            Columns = EmptyColumns(layout)
        }));

    public Task MoveUpAsync(string slug, IReadOnlyList<TreeStep> path) =>
        MutateAsync(slug, tree => ContentTreeEditor.MoveUp(tree, path));

    public Task MoveDownAsync(string slug, IReadOnlyList<TreeStep> path) =>
        MutateAsync(slug, tree => ContentTreeEditor.MoveDown(tree, path));

    public Task DeleteAsync(string slug, IReadOnlyList<TreeStep> path) =>
        MutateAsync(slug, tree => ContentTreeEditor.Remove(tree, path));

    public Task UpdateWidgetAsync(string slug, IReadOnlyList<TreeStep> path, JsonObject props) =>
        MutateAsync(slug, tree =>
        {
            if (ContentTreeEditor.NodeAt(tree, path) is WidgetNode widget)
                widget.Props = props;
        });

    private async Task MutateAsync(string slug, Action<List<ContentNode>> edit)
    {
        var page = await repository.GetBySlugAsync(slug)
            ?? throw new InvalidOperationException($"Content page '{slug}' not found.");

        var tree = (ContentPageJson.Deserialize(page.DraftContent) ?? []).ToList();
        edit(tree);
        HeadingAnchorizer.Apply(tree);

        await repository.UpdateDraftAsync(page.Id, ContentPageJson.Serialize(tree));
    }

    private static List<List<ContentNode>> EmptyColumns(RegionLayout layout) =>
        Enumerable.Range(0, RegionLayoutGrid.ColumnCount(layout))
            .Select(_ => new List<ContentNode>())
            .ToList();
}
