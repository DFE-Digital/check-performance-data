using DfE.CheckPerformanceData.Application.Media.Folders;
using DfE.CheckPerformanceData.Application.Media.Reports;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Media library reports, gated on the media section. Both pages only read. The alt text report
// lists library images and uses on current pages and content blocks whose alt text is missing or
// weak, each list paged on its own at the CMS page length. The size report shows the library
// totals and the largest images, up to a fixed number. Both cover every folder, and name each
// image's folder.
[RequireAdminSection(AdminNavKeys.Media)]
public sealed class MediaReportsController(IMediaReportService reports, ISettingService settings, IMediaFolderService folders) : Controller
{
    public const string AltTextView = "~/Views/MediaLibrary/AltTextReport.cshtml";
    public const string SizeView = "~/Views/MediaLibrary/SizeReport.cshtml";

    /// <summary>How many of the largest images the size report lists.</summary>
    public const int LargestCount = 50;

    private const int DefaultPageSize = 20;

    /// <summary>The ViewData key of the folder tree, so each image row can show its folder.</summary>
    public const string FoldersKey = "MediaFolders";

    [HttpGet("admin/media/reports/alt-text")]
    public async Task<IActionResult> AltText(int imagesPage = 1, int usesPage = 1, CancellationToken ct = default)
    {
        var report = await reports.GetAltTextReportAsync(ct);
        ViewData[FoldersKey] = await folders.GetTreeAsync(ct);
        var size = await settings.GetIntAsync(SettingKeys.CmsPageLength);
        if (size <= 0) size = DefaultPageSize;
        return View(AltTextView, new MediaAltTextReportViewModel(
            MediaReportList<MediaAltTextAssetRow>.Slice(report.Assets, imagesPage, size),
            MediaReportList<MediaAltTextUseRow>.Slice(report.Uses, usesPage, size)));
    }

    [HttpGet("admin/media/reports/size")]
    public async Task<IActionResult> Size(CancellationToken ct = default)
    {
        ViewData[FoldersKey] = await folders.GetTreeAsync(ct);
        return View(SizeView, await reports.GetSizeReportAsync(LargestCount, ct));
    }
}
