using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// CMS media library admin. Gated on the media section, which admins and content editors hold by
// default. Lists the library's images one page at a time, most recently changed first.
[RequireAdminSection(AdminNavKeys.Media)]
public sealed class MediaLibraryController(IMediaAssetRepository media, ISettingService settings) : Controller
{
    [HttpGet("admin/media")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var pageSize = Math.Max(1, await settings.GetIntAsync(SettingKeys.CmsPageLength));
        var result = await media.ListAsync(new MediaAssetQuery(Page: Math.Max(1, page), PageSize: pageSize), ct);
        return View(new MediaLibraryIndexViewModel { Page = result });
    }
}
