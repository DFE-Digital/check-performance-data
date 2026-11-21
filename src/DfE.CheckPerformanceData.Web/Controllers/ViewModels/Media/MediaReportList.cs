namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>One page of a report list. <see cref="Page"/> is 1-based; <see cref="Total"/> counts every row.</summary>
public sealed record MediaReportList<T>(IReadOnlyList<T> Items, int Page, int TotalPages, int Total)
{
    /// <summary>The rows of one page. A page out of range shows the nearest page. An empty list has one empty page.</summary>
    public static MediaReportList<T> Slice(IReadOnlyList<T> all, int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var totalPages = Math.Max(1, (all.Count + pageSize - 1) / pageSize);
        var current = Math.Clamp(page, 1, totalPages);
        return new MediaReportList<T>(all.Skip((current - 1) * pageSize).Take(pageSize).ToList(), current, totalPages, all.Count);
    }
}
