namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Render-time wrapper passed to the recursive _AdminLandingTile partial: the node to render
// plus the heading level to use for it. HeadingLevel tracks depth so the document outline stays
// valid (WCAG 2.2 AA) — top-level group sections are <h2>, their tiles/sub-headings <h3>, nested
// tiles <h4>, and so on (clamped to <h6>).
public sealed record AdminLandingNodePartialModel(
    AdminNavNodeViewModel Node,
    int HeadingLevel)
{
    // Heading tag for this node, clamped to the valid h1-h6 range.
    public string HeadingTag => $"h{Math.Clamp(HeadingLevel, 2, 6)}";

    // Wrapper for a child, one heading level deeper.
    public AdminLandingNodePartialModel ForChild(AdminNavNodeViewModel child) => new(child, HeadingLevel + 1);
}
