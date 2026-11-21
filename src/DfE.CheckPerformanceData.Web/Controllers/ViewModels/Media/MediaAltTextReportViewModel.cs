using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// The alt text report page: one page of library images and one page of uses. Each use links to
/// where its text is fixed: the image's own page when readers get the library's text, otherwise
/// the page or content block editor.
/// </summary>
public sealed record MediaAltTextReportViewModel(
    MediaReportList<MediaAltTextAssetRow> Images,
    MediaReportList<MediaAltTextUseRow> Uses)
{
    public const string Path = "/admin/media/reports/alt-text";

    public string ImagesPageUrl(int page) => Url(page, Uses.Page, "images");

    public string UsesPageUrl(int page) => Url(Images.Page, page, "uses");

    public static string ImageUrl(Guid assetId) => $"/admin/media/{assetId}";

    public static string FixUrl(MediaAltTextUseRow row)
    {
        if (row.FromLibrary) return ImageUrl(row.AssetId);
        if (row.PageNodeId is { } page) return $"/admin/pages/{page}";
        var key = Uri.EscapeDataString(row.BlockKey ?? "");
        return $"/admin/content-blocks?edit={key}#edit-{key}";
    }

    public static string FixText(MediaAltTextUseRow row) =>
        row.FromLibrary ? "Edit image" : row.PageNodeId is not null ? "Edit page" : "Edit content block";

    /// <summary>The current versions of the page that show the use, such as "Live and draft".</summary>
    public static string Versions(MediaAltTextUseRow row)
    {
        if (row.States.Count == 0) return "Content block";
        var words = row.States.Select(s => s switch
        {
            EmbeddedVersionState.Live => "live",
            EmbeddedVersionState.Scheduled => "scheduled",
            _ => "draft",
        }).ToList();
        var text = words.Count == 1 ? words[0] : string.Join(", ", words.SkipLast(1)) + " and " + words[^1];
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Url(int imagesPage, int usesPage, string anchor) =>
        $"{Path}?imagesPage={imagesPage}&usesPage={usesPage}#{anchor}";
}
