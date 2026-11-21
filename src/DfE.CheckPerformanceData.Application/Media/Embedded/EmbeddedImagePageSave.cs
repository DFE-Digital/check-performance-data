namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Everything saved for one page in one go: the content of the new versions that replace its
/// current versions, and what
/// the audit log records about the page (how many images moved, and the library images used).
/// </summary>
public sealed record EmbeddedImagePageSave(
    EmbeddedImagePage Page,
    IReadOnlyList<EmbeddedImageVersionChange> Versions,
    int ImagesMoved,
    IReadOnlyList<Guid> AssetIds);
