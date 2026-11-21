namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// What a run did. ImagesMoved counts the image tags changed, on every current version.
/// ImagesCreated counts new library images; DuplicatesReused counts images the library already held.
/// </summary>
public sealed record EmbeddedImageMoveResult(
    int PagesChanged,
    int ImagesMoved,
    int ImagesCreated,
    int DuplicatesReused,
    IReadOnlyList<EmbeddedImagePageFailure> Failures);
