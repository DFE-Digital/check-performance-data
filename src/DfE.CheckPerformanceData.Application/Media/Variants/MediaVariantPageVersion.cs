using DfE.CheckPerformanceData.Application.Media.Embedded;

namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// A current page version (live, scheduled or the working draft) with its content, and the images
/// that its <c>media_usages</c> rows record.
/// </summary>
public sealed record MediaVariantPageVersion(
    Guid PageNodeId,
    string Title,
    EmbeddedVersionState State,
    string Content,
    IReadOnlyList<Guid> RecordedAssetIds);
