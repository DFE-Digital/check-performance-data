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
    ISettingService settings,
    IMediaDeletionService deletions) : Controller
{
    private const string IndexView = "Index";
    private const string DetailView = "Detail";
    private const string DeleteView = "Delete";
    private const int PreviewWidth = 960;
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

    /// <summary>A replace carries one file of at most 20 MB, plus room for the form fields.</summary>
    public const long MaxReplaceRequestBytes = MediaUploadLimits.MaxFileBytes + 1024 * 1024;

    public const string NoReplaceFileError = "Select an image to upload";

    /// <summary>The replace form's file control: its field name, id and model-state key.</summary>
    public const string ReplaceFileField = "file";

    /// <summary>The TempData key of the success banner on the library and image pages.</summary>
    public const string SuccessKey = "MediaLibrarySuccess";

    [HttpGet("admin/media")]
    public async Task<IActionResult> Index(string? search, bool missingAlt, bool unused, string? sort, int page = 1, CancellationToken ct = default)
    {
        return View(IndexView, await BuildIndexAsync(search, missingAlt, unused, sort, page, ct));
    }

    // One image: a large preview, its facts and public link, the "Image details" form, the
    // replace form, its version history and the pages that use it.
    [HttpGet("admin/media/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var model = await BuildDetailAsync(id, null, ct);
        return model is null ? NotFound() : View(DetailView, model);
    }

    // Saves alt text, caption, credit and tags. The generic audit capture records the change.
    // With errors, the page shows again with what was typed and an error summary.
    [HttpPost("admin/media/{id:guid}/details")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDetails(Guid id, [Bind(Prefix = "Metadata")] MediaMetadataForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var model = await BuildDetailAsync(id, form, ct);
            if (model is null) return NotFound();
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(DetailView, model);
        }

        if (!await media.UpdateMetadataAsync(id, form.ToMetadata(), ct)) return NotFound();
        TempData[SuccessKey] = "Image details saved";
        return Redirect(MediaLibraryItemViewModel.DetailUrlFor(id));
    }

    // Stores a new version through the upload pipeline, with the same checks as an upload. A
    // refused file, or a replace that lost a race with another, shows its reason on the file control.
    [HttpPost("admin/media/{id:guid}/replace")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxReplaceRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxReplaceRequestBytes)]
    public async Task<IActionResult> Replace(Guid id, IFormFile? file, CancellationToken ct)
    {
        string error;
        if (file is null || string.IsNullOrEmpty(file.FileName))
        {
            error = NoReplaceFileError;
        }
        else
        {
            MediaReplaceResult result;
            await using (var stream = file.OpenReadStream())
                result = await uploads.ReplaceAsync(id, stream, ct);

            switch (result.Outcome)
            {
                case MediaReplaceOutcome.NotFound:
                    return NotFound();
                case MediaReplaceOutcome.Replaced:
                    TempData[SuccessKey] = $"Version {result.Version!.Version} uploaded";
                    return Redirect(MediaLibraryItemViewModel.DetailUrlFor(id));
            }
            error = result.Rejection!;
        }

        var model = await BuildDetailAsync(id, null, ct);
        if (model is null) return NotFound();
        ModelState.AddModelError(ReplaceFileField, error);
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(DetailView, model);
    }

    // The delete page. An image that any page version uses lists those pages instead of the button.
    [HttpGet("admin/media/{id:guid}/delete")]
    public async Task<IActionResult> DeleteConfirm(Guid id, CancellationToken ct)
    {
        var model = await BuildDeleteAsync(id, ct);
        return model is null ? NotFound() : View(DeleteView, model);
    }

    // The service checks the usages again, so a page that started to use the image after the
    // delete page was shown still stops the delete.
    [HttpPost("admin/media/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var asset = await media.GetAsync(id, ct);
        switch (await deletions.DeleteAsync(id, ct))
        {
            case MediaDeleteOutcome.Deleted:
                TempData[SuccessKey] = $"{asset?.FileName ?? "The image"} deleted";
                return Redirect("/admin/media");
            case MediaDeleteOutcome.InUse:
                var model = await BuildDeleteAsync(id, ct);
                if (model is null) return NotFound();
                Response.StatusCode = StatusCodes.Status409Conflict;
                return View(DeleteView, model);
            default:
                return NotFound();
        }
    }

    private async Task<MediaDetailViewModel?> BuildDetailAsync(Guid id, MediaMetadataForm? form, CancellationToken ct)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var asset = await media.GetAsync(id, ct);
        if (asset is null) return null;

        var versions = await media.GetVersionsAsync(id, ct);
        var latest = asset.Latest;
        return new MediaDetailViewModel
        {
            Item = MediaLibraryItemViewModel.From(asset, urls),
            PreviewUrl = urls.Rendition(asset, latest, PreviewWidth),
            PublicUrl = $"{Request.Scheme}://{Request.Host}{urls.Original(asset, latest)}",
            UploadedBy = latest.UploadedBy,
            UploadedAtUtc = latest.UploadedAtUtc,
            Versions = versions.Select(v => new MediaVersionItemViewModel(
                v.Version,
                urls.Rendition(asset, v, MediaLibraryItemViewModel.ThumbnailWidth),
                !MediaUrlBuilder.IsResizable(v),
                v.UploadedAtUtc,
                v.UploadedBy,
                MediaLibraryItemViewModel.DimensionsOf(v),
                MediaFileSize.Format(v.SizeBytes),
                v.Version == asset.CurrentVersion)).ToList(),
            UsagePages = await media.GetUsagePagesAsync(id, ct),
            Metadata = form ?? MediaMetadataForm.From(asset),
        };
    }

    private async Task<MediaDeleteViewModel?> BuildDeleteAsync(Guid id, CancellationToken ct)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var asset = await media.GetAsync(id, ct);
        if (asset is null) return null;
        return new MediaDeleteViewModel
        {
            Item = MediaLibraryItemViewModel.From(asset, urls),
            UsagePages = await media.GetUsagePagesAsync(id, ct),
        };
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
