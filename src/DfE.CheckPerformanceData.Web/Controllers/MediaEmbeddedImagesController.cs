using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Moves images embedded in page content as data: URIs into the media library. Gated on the media
// section. The GET is a dry run that changes nothing: it lists the pages and counts the images.
// The POST does the move and shows what it did. Only current page versions change; older
// versions keep their embedded images. Each page is audit-logged, and so is each run.
[RequireAdminSection(AdminNavKeys.Media)]
public sealed class MediaEmbeddedImagesController(IEmbeddedImageMover mover) : Controller
{
    public const string DryRunView = "~/Views/MediaLibrary/MoveEmbedded.cshtml";
    public const string ResultView = "~/Views/MediaLibrary/MoveEmbeddedResult.cshtml";

    [HttpGet("admin/media/move-embedded")]
    public async Task<IActionResult> DryRun(CancellationToken ct) =>
        View(DryRunView, await mover.ScanAsync(ct));

    // The result is shown straight away rather than after a redirect: it is the record of this
    // run, and running again is safe because a second run finds nothing to do.
    [HttpPost("admin/media/move-embedded")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(CancellationToken ct) =>
        View(ResultView, await mover.MoveAsync(ct));
}
