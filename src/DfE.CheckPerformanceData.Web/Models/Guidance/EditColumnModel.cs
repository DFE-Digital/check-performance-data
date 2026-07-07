using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// Model for the recursive editor column partial. ActionBase is the URL prefix for mutation
// form actions (e.g. "/content-page/my-slug" or "/admin/pages/{guid}/content"). ParentPath is
// the path to the region whose column this is (empty at the root); ColumnIndex selects that
// column. Together they let each child node compute its own tree path for the add/move/delete forms.
public sealed record EditColumnModel(
    string ActionBase,
    IReadOnlyList<ContentNode> Nodes,
    IReadOnlyList<TreeStep> ParentPath,
    int ColumnIndex)
{
    public IReadOnlyList<TreeStep> PathOf(int index) => [.. ParentPath, new TreeStep(ColumnIndex, index)];

    // Path that appends to the end of this column (insert position == current count).
    public IReadOnlyList<TreeStep> AppendPath() => [.. ParentPath, new TreeStep(ColumnIndex, Nodes.Count)];
}
