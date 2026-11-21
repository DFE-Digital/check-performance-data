namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// The dry run: the pages whose current versions hold embedded images, the content blocks that
/// hold them, and totals. ImageCount counts every image on those pages and blocks.
/// UniqueImageCount and UniqueBytes count each different file once. The library finds duplicates
/// after cleaning, so a run can find a few more. OlderVersionCount is the page versions that hold
/// embedded images but are left as they are.
/// </summary>
public sealed record EmbeddedImageScan(
    IReadOnlyList<EmbeddedImagePageScan> Pages,
    int VersionCount,
    int ImageCount,
    int UniqueImageCount,
    long UniqueBytes,
    int OlderVersionCount,
    IReadOnlyList<EmbeddedImageBlockScan> Blocks)
{
    public int PagesToMoveCount => Pages.Count(p => p.CanMove);

    public int BlocksToMoveCount => Blocks.Count(b => b.CanMove);

    public int ImagesToMoveCount =>
        Pages.Where(p => p.CanMove).Sum(p => p.ImageCount) + Blocks.Where(b => b.CanMove).Sum(b => b.ImageCount);
}
