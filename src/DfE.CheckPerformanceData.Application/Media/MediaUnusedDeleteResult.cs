namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// The outcome of one "Delete unused images" run. Deleted: the images removed. InUse: images that
/// content started to use after the confirmation page, so they stay. NotFound: images already gone.
/// </summary>
public sealed record MediaUnusedDeleteResult(
    Guid RunId,
    IReadOnlyList<Guid> Deleted,
    IReadOnlyList<Guid> InUse,
    IReadOnlyList<Guid> NotFound);
