using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Folders;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using DfE.CheckPerformanceData.Web.PageTree;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The media picker that page editors open from the Image widget and the rich text editor. It is
// gated on the media section, like the rest of the library: an editor without it gets no picker,
// and the editors still work with a typed image id.
// media-picker.js calls these endpoints. The upload takes one file and needs the antiforgery
// token in the X-XSRF-TOKEN header. The preview renders the Image widget's own view, so it shows
// exactly what the page will show.
[RequireAdminSection(AdminNavKeys.Media)]
public sealed class MediaPickerController(
    IMediaAssetRepository media,
    IMediaUploadService uploads,
    IMediaUrlBuilder urls,
    IMediaFolderService folders) : Controller
{
    /// <summary>How many images one page of the picker shows.</summary>
    public const int PageSize = 12;

    /// <summary>
    /// One file of at most 20 MB, plus room for the multipart framing. Kestrel's own limit is off
    /// app-wide, so the action sets one. The upload pipeline checks the 20 MB file limit itself.
    /// </summary>
    public const long MaxUploadRequestBytes = MediaUploadLimits.MaxFileBytes + 1024 * 1024;

    public const string NoFileError = "Select an image to upload";

    public const string ImageWidgetView = "~/Views/Shared/ContentPages/Widgets/_Image.cshtml";

    // One page of the library, newest first, as JSON. The search matches the file name, alt text,
    // caption, credit or a tag. "folder" and "subfolders" choose the folder as the library page
    // does: no folder with subfolders (the default) is the whole library. An unknown folder is 404.
    [HttpGet("admin/media/picker")]
    public async Task<IActionResult> List(string? q, int page = 1, Guid? folder = null, bool subfolders = true, CancellationToken ct = default)
    {
        if (folder is Guid id && (await folders.GetTreeAsync(ct)).Find(id) is null) return NotFound();

        var term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var result = await media.ListAsync(new MediaAssetQuery(
            Search: term,
            Page: Math.Max(1, page),
            PageSize: PageSize,
            Sort: MediaAssetSort.Newest,
            FolderId: folder,
            IncludeSubfolders: subfolders), ct);

        var size = Math.Max(1, result.PageSize);
        return new JsonResult(new
        {
            items = result.Items.Select(a => MediaPickerItem.From(a, urls)).ToList(),
            page = result.Page,
            totalPages = Math.Max(1, (result.TotalCount + size - 1) / size),
            totalCount = result.TotalCount,
        });
    }

    // Puts one file in the library through the upload pipeline, in the folder the picker shows
    // ("folder", none for the top). A new image or a file that is already in the library answers
    // the image; a refused file, or a folder that is gone, answers the reason with a 400.
    [HttpPost("admin/media/picker/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<IActionResult> Upload(IFormFile? file, Guid? folder = null, CancellationToken ct = default)
    {
        if (file is null || string.IsNullOrEmpty(file.FileName)) return Error(NoFileError);

        MediaUploadResult result;
        await using (var stream = file.OpenReadStream())
            result = await uploads.UploadAsync(stream, file.FileName, folder, ct);

        return result.Outcome switch
        {
            MediaUploadOutcome.Created => new JsonResult(MediaPickerItem.From(result.Asset!, urls)),
            MediaUploadOutcome.Duplicate => new JsonResult(MediaPickerItem.From(result.Asset!, urls, duplicate: true)),
            _ => Error(result.Rejection!),
        };
    }

    // The Image widget as the page would show it, from the editor's unsaved values in the query.
    // Only the widget's own props are read. The view leaves out a value that would be refused.
    [HttpGet("admin/media/picker/preview")]
    public IActionResult Preview()
    {
        var props = WidgetRegistry.CreateDefaultProps(ImageWidgetProps.Type);
        foreach (var key in props.Select(p => p.Key).ToList())
        {
            if (Request.Query.TryGetValue(key, out var value)) props[key] = value.ToString();
        }

        var result = PartialView(ImageWidgetView, new WidgetNode { Type = ImageWidgetProps.Type, Props = props });
        result.ViewData[MediaRenderContext.ViewDataKey] = new MediaRenderContext(null, ShowEditorNotices: true);
        return result;
    }

    private static JsonResult Error(string message) =>
        new(new { error = message }) { StatusCode = StatusCodes.Status400BadRequest };
}
