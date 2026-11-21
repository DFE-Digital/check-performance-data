namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// The dry run: the pages whose current versions hold embedded images, and totals.
/// UniqueImageCount and UniqueBytes count each different file once. The library finds duplicates
/// after cleaning, so a run can find a few more. OlderVersionCount and ContentBlockCount are the
/// page versions and content blocks that hold embedded images but are left as they are.
/// </summary>
public sealed record EmbeddedImageScan(
    IReadOnlyList<EmbeddedImagePageScan> Pages,
    int VersionCount,
    int ImageCount,
    int UniqueImageCount,
    long UniqueBytes,
    int OlderVersionCount,
    int ContentBlockCount)
{
    public int PagesToMoveCount => Pages.Count(p => p.CanMove);

    public int ImagesToMoveCount => Pages.Where(p => p.CanMove).Sum(p => p.ImageCount);
}
