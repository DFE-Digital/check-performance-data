using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// Where a library image is used, split for the image page and the delete page. Current pages are
/// pages whose live, scheduled or draft version shows the image. Older pages are pages where only
/// older versions hold it, and deleted pages. Any usage blocks a delete, because older versions
/// must still show the file they were saved with.
/// </summary>
public sealed class MediaUsageSummary(IReadOnlyList<MediaUsagePage> pages)
{
    public const string OnlyInOlderVersionsMessage =
        "This image is only used in older versions of pages. It can't be deleted while those versions are kept.";

    public IReadOnlyList<MediaUsagePage> CurrentPages { get; } =
        pages.Where(p => p.InCurrentVersion && !p.IsDeleted).ToList();

    public IReadOnlyList<MediaUsagePage> OlderPages { get; } =
        pages.Where(p => !p.InCurrentVersion || p.IsDeleted).ToList();

    public bool IsUsed => pages.Count > 0;

    public bool OnlyInOlderVersions => IsUsed && CurrentPages.Count == 0;

    /// <summary>"1 page", "N pages", "Only in older versions of N pages" or "Not used on any page".</summary>
    public string UsedOn =>
        !IsUsed ? "Not used on any page"
        : OnlyInOlderVersions ? $"Only in older versions of {Pages(OlderPages.Count)}"
        : Pages(CurrentPages.Count);

    private static string Pages(int count) => count == 1 ? "1 page" : $"{count} pages";
}
