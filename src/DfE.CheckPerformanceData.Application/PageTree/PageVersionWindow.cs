namespace DfE.CheckPerformanceData.Application.PageTree;

// A version's publish window. A superseded version was replaced by a newer version and is never
// live, whatever its window says.
public sealed record PageVersionWindow(int VersionId, DateTime? PublishFrom, DateTime? PublishTo, bool Superseded = false);
