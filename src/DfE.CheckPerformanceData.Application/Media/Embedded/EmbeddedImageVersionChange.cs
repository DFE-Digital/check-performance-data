namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// New content for one page version. The change is made only while the version still holds
/// <see cref="ExpectedContent"/>, so an editor's save during the move is never overwritten.
/// </summary>
public sealed record EmbeddedImageVersionChange(Guid VersionRowId, string ExpectedContent, string Content);
