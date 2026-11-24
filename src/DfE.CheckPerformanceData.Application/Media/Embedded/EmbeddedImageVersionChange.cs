namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// New content for one current page version, saved as a new version that replaces it. The change
/// is made only while the version is still current and still holds <see cref="ExpectedContent"/>,
/// so an editor's save or publish during the move is never overwritten.
/// </summary>
public sealed record EmbeddedImageVersionChange(Guid VersionRowId, string ExpectedContent, string Content);
