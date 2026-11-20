using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// CMS media library admin. Gated on the media section, which admins and content editors hold by
// default. The index lists the library as a grid of thumbnails, one CMS page length at a time,
// with a search, two filters and a sort. The upload form works as a plain multipart post; with
// JavaScript, media-library-upload.js posts one file per request and asks for JSON instead.
[RequireAdminSection(AdminNavKeys.Media)]
public sealed class MediaLibraryController(
    IMediaAssetRepository media,
    IMediaUploadService uploads,
    IMediaUrlBuilder urls,
    ISettingService settings) : Controller
{
    private const string IndexView = "Index";
    private const int DefaultPageSize = 20;
    private const string SortByName = "name";
    private const string SortNewest = "newest";

    /// <summary>
    /// The most one upload request may carry. Kestrel's own limit is off app-wide, so the action
    /// sets one. Each file must also be under <see cref="MediaUploadLimits.MaxFileBytes"/> (20 MB),
    /// which the upload pipeline checks. 50 MB lets a plain form post carry a few files, and
    /// matches the cluster ingress, which refuses a larger body anyway.
    /// </summary>
    public const long MaxUploadRequestBytes = 50L * 1024 * 1024;

    public const string NoFileError = "Select one or more images to upload";

    [HttpGet("admin/media")]
    public async Task<IActionResult> Index(string? search, bool missingAlt, bool unused, string? sort, int page = 1, CancellationToken ct = default)
    {
        return View(IndexView, await BuildIndexAsync(search, missingAlt, unused, sort, page, ct));
    }

    // A minimal view of one image, so each library thumbnail has somewhere to go.
    [HttpGet("admin/media/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var asset = await media.GetAsync(id, ct);
        if (asset is null) return NotFound();
        return View(MediaLibraryItemViewModel.From(asset, urls));
    }

    // Each file goes through the upload pipeline on its own: one bad file does not stop the rest.
    // A script call (Accept: application/json, token in the X-XSRF-TOKEN header) gets the results
    // as JSON. A plain form post gets the library page again, with the results above the grid.
    [HttpPost("admin/media/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<IActionResult> Upload(List<IFormFile> files, CancellationToken ct)
    {
        var chosen = (files ?? []).Where(f => !string.IsNullOrEmpty(f.FileName)).ToList();
        if (chosen.Count == 0)
        {
            if (WantsJson) return new JsonResult(new { error = NoFileError }) { StatusCode = StatusCodes.Status400BadRequest };
            Response.StatusCode = StatusCodes.Status400BadRequest;
            var empty = await BuildIndexAsync(null, false, false, null, 1, ct);
            return View(IndexView, new MediaLibraryIndexViewModel
            {
                Page = empty.Page, Items = empty.Items, UploadError = NoFileError
            });
        }

        var results = new List<MediaUploadItemViewModel>();
        foreach (var file in chosen)
        {
            await using var stream = file.OpenReadStream();
            var result = await uploads.UploadAsync(stream, file.FileName, ct);
            results.Add(ToItem(file.FileName, result));
        }

        if (WantsJson) return new JsonResult(results);

        var index = await BuildIndexAsync(null, false, false, null, 1, ct);
        return View(IndexView, new MediaLibraryIndexViewModel
        {
            Page = index.Page, Items = index.Items, UploadResults = results
        });
    }

    private bool WantsJson =>
        Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

    private MediaUploadItemViewModel ToItem(string fileName, MediaUploadResult result) => result.Outcome switch
    {
        MediaUploadOutcome.Created => new(result.Outcome, result.Asset!.FileName,
            MediaLibraryItemViewModel.DetailUrlFor(result.Asset.Id), Thumbnail(result.Asset), null, null),
        MediaUploadOutcome.Duplicate => new(result.Outcome, Path.GetFileName(fileName),
            MediaLibraryItemViewModel.DetailUrlFor(result.Asset!.Id), Thumbnail(result.Asset), result.Asset.FileName, null),
        _ => new(result.Outcome, Path.GetFileName(fileName), null, null, null, result.Rejection),
    };

    private string Thumbnail(MediaAssetDto asset) =>
        urls.Rendition(asset, asset.Latest, MediaLibraryItemViewModel.ThumbnailWidth);

    private async Task<MediaLibraryIndexViewModel> BuildIndexAsync(
        string? search, bool missingAlt, bool unused, string? sort, int page, CancellationToken ct)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var byName = string.Equals(sort, SortByName, StringComparison.OrdinalIgnoreCase);
        var result = await media.ListAsync(new MediaAssetQuery(
            Search: term,
            MissingAltText: missingAlt,
            UnusedOnly: unused,
            Page: Math.Max(1, page),
            PageSize: await ResolvePageSizeAsync(),
            Sort: byName ? MediaAssetSort.Name : MediaAssetSort.Newest), ct);

        return new MediaLibraryIndexViewModel
        {
            Page = result,
            Items = result.Items.Select(a => MediaLibraryItemViewModel.From(a, urls)).ToList(),
            Search = term,
            MissingAlt = missingAlt,
            Unused = unused,
            Sort = byName ? SortByName : SortNewest,
        };
    }

    private async Task<int> ResolvePageSizeAsync()
    {
        var size = await settings.GetIntAsync(SettingKeys.CmsPageLength);
        return size > 0 ? size : DefaultPageSize;
    }
}
