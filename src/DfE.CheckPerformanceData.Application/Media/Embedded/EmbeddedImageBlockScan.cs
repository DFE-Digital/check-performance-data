namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// What a move would do to one content block: how many images it holds (an image used twice
/// counts twice), and the images that cannot move. A block with any refusal is left as it is.
/// </summary>
public sealed record EmbeddedImageBlockScan(
    EmbeddedImageBlock Block,
    int ImageCount,
    IReadOnlyList<EmbeddedImageRefusal> Refusals)
{
    public bool CanMove => Refusals.Count == 0;
}
