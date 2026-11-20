namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>One page of the library, most recently changed first. Page is clamped to [1, TotalPages].</summary>
public sealed record MediaAssetPage(IReadOnlyList<MediaAssetDto> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
}
