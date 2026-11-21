namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// What a move would do to one page: which current versions hold embedded images, how many
/// images they hold (an image used twice counts twice), and the images that cannot move. A page
/// with any refusal is left as it is, so its images stay together.
/// </summary>
public sealed record EmbeddedImagePageScan(
    EmbeddedImagePage Page,
    IReadOnlyList<EmbeddedVersionState> Versions,
    int ImageCount,
    IReadOnlyList<EmbeddedImageRefusal> Refusals)
{
    public bool CanMove => Refusals.Count == 0;
}
