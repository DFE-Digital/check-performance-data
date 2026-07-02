using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// Model for the recursive "Add content here" partial. InsertPath addresses the position to insert
// at (a path that ends in the column and index the new node should occupy — see TreeStep). The
// same partial is rendered below each existing node in a column and once at the top for an empty
// column, so an editor can insert content at any position without scrolling.
public sealed record AddHereModel(
    string ActionBase,
    IReadOnlyList<TreeStep> InsertPath);
