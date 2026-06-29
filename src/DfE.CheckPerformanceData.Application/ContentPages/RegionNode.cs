namespace DfE.CheckPerformanceData.Application.ContentPages;

// A layout container. Splits its area into columns per <see cref="Layout"/>; each column is an
// ordered list of child nodes, which may themselves be regions (nesting). Optional named style
// is constrained to an allow-list by the renderer.
public sealed class RegionNode : ContentNode
{
    public RegionLayout Layout { get; init; } = RegionLayout.Single;
    public string? Style { get; init; }
    public List<List<ContentNode>> Columns { get; init; } = [];
}
