using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Folders;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// CMS media library admin. Gated on the media section, which admins and content editors hold by
// default. The index lists one folder of the library (the "folder" query value; none is the top)
// as a grid, a details table or large images (the "view" query value), one CMS page length at a
// time, with a search, two filters, "Include subfolders" and a sort. The upload form works as a
// plain multipart post; with JavaScript, media-library-upload.js posts one file per request and
// asks for JSON instead. The folder actions and the bulk image move are here too, under the same
// gate; the generic audit capture records each of them.
[RequireAdminSection(AdminNavKeys.Media)]
public sealed class MediaLibraryController(
    IMediaAssetRepository media,
    IMediaUploadService uploads,
    IMediaUrlBuilder urls,
    ISettingService settings,
    IMediaDeletionService deletions,
    IMediaFolderService folders) : Controller
{
    private const string IndexView = "Index";
    private const string DetailView = "Detail";
    private const string DeleteView = "Delete";
    private const string DeleteUnusedView = "DeleteUnused";
    private const string FolderView = "Folder";
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

    /// <summary>The TempData key of a refused "Move selected images", shown as an error summary.</summary>
    public const string MoveErrorKey = "MediaLibraryMoveError";

    /// <summary>The value of a folder select that means the top of the library.</summary>
    public const string TopLevel = "top";

    public const string NoMoveTargetError = "Select a folder to move the images to";
    public const string NoFolderTargetError = "Select where to move the folder";

    // An unknown folder id gets 404. "subfolders" is true unless the query says false: the
    // "Include subfolders" check box sends true then a hidden false, and the first value wins.
    [HttpGet("admin/media")]
    public async Task<IActionResult> Index(string? search, bool missingAlt, bool unused, string? sort, string? view, int page = 1,
        Guid? folder = null, bool subfolders = true, CancellationToken ct = default)
    {
        var model = await BuildIndexAsync(new LibraryState(search, missingAlt, unused, sort, view, page, folder, subfolders), ct);
        if (model is null) return NotFound();
        if (TempData[MoveErrorKey] is string moveError)
        {
            ViewData["HasError"] = true;
            model.MoveError = moveError;
        }
        return View(IndexView, model);
    }

    // "Create a folder" on the library page: the new folder goes inside the folder shown. A refused
    // name shows the library page again, with the error on the name field.
    [HttpPost("admin/media/folders")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder(string? name, Guid? parent, CancellationToken ct)
    {
        var result = await folders.CreateAsync(name, parent, ct);
        if (result.Succeeded)
        {
            TempData[SuccessKey] = $"Folder ‘{result.Folder!.Name}’ created";
            return Redirect(MediaFolderViewModel.LibraryUrlFor(parent));
        }

        var shown = result.Outcome == MediaFolderOutcome.ParentNotFound ? null : parent;
        var model = await BuildIndexAsync(new LibraryState(Folder: shown), ct);
        if (model is null) return NotFound();
        Response.StatusCode = StatusCodes.Status400BadRequest;
        ViewData["HasError"] = true;
        model.NewFolderName = name;
        model.NewFolderError = result.Error;
        return View(IndexView, model);
    }

    // One folder's page: rename it, move it, or delete it when it is empty.
    [HttpGet("admin/media/folders/{id:guid}")]
    public async Task<IActionResult> Folder(Guid id, CancellationToken ct)
    {
        var model = await BuildFolderAsync(id, null, null, null, null, ct);
        return model is null ? NotFound() : View(FolderView, model);
    }

    [HttpPost("admin/media/folders/{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFolder(Guid id, string? name, CancellationToken ct)
    {
        var result = await folders.RenameAsync(id, name, ct);
        if (result.Outcome == MediaFolderOutcome.NotFound) return NotFound();
        if (result.Succeeded)
        {
            TempData[SuccessKey] = $"Folder renamed to ‘{result.Folder!.Name}’";
            return Redirect(MediaFolderViewModel.FolderSettingsUrl(id));
        }
        return await FolderErrorAsync(id, name, result.Error, null, null, StatusCodes.Status400BadRequest, ct);
    }

    // "parent" is a folder id, or "top" for the top of the library.
    [HttpPost("admin/media/folders/{id:guid}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveFolder(Guid id, string? parent, CancellationToken ct)
    {
        if (!TryParseTarget(parent, out var target))
            return await FolderErrorAsync(id, null, null, NoFolderTargetError, null, StatusCodes.Status400BadRequest, ct);

        var result = await folders.MoveAsync(id, target, ct);
        if (result.Outcome == MediaFolderOutcome.NotFound) return NotFound();
        if (result.Succeeded)
        {
            TempData[SuccessKey] = $"Folder ‘{result.Folder!.Name}’ moved";
            return Redirect(MediaFolderViewModel.FolderSettingsUrl(id));
        }
        return await FolderErrorAsync(id, null, null, result.Error, null, StatusCodes.Status400BadRequest, ct);
    }

    // Only an empty folder is deleted. The page says what a full folder holds; the service and
    // the database refuse it again.
    [HttpPost("admin/media/folders/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct)
    {
        var result = await folders.DeleteAsync(id, ct);
        if (result.Outcome == MediaFolderOutcome.NotFound) return NotFound();
        if (result.Succeeded)
        {
            TempData[SuccessKey] = $"Folder ‘{result.Folder!.Name}’ deleted";
            return Redirect(MediaFolderViewModel.LibraryUrlFor(result.Folder.ParentId));
        }
        return await FolderErrorAsync(id, null, null, null, result.Error, StatusCodes.Status409Conflict, ct);
    }

    // "Move selected images": the ticked images go into one folder, or to the top ("top"). The
    // editor goes back to the list they were on, where a refusal shows as an error summary.
    [HttpPost("admin/media/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveImages(List<Guid>? ids, string? target, string? returnUrl, CancellationToken ct)
    {
        var back = SafeReturnUrl(returnUrl);
        if (!TryParseTarget(target, out var folderId))
        {
            TempData[MoveErrorKey] = NoMoveTargetError;
            return Redirect(back);
        }

        var result = await folders.MoveImagesAsync(ids ?? [], folderId, ct);
        if (!result.Succeeded)
        {
            TempData[MoveErrorKey] = result.Error;
            return Redirect(back);
        }

        var tree = await folders.GetTreeAsync(ct);
        var where = folderId is null ? "the top of the library" : $"‘{tree.PathText(folderId)}’";
        TempData[SuccessKey] = result.Moved == 1 ? $"1 image moved to {where}" : $"{result.Moved} images moved to {where}";
        return Redirect(back);
    }

    /// <summary>A library address on this site, or the library itself for anything else.</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is not null
        && (returnUrl == "/admin/media" || returnUrl.StartsWith("/admin/media?", StringComparison.Ordinal))
            ? returnUrl
            : "/admin/media";

    private static bool TryParseTarget(string? value, out Guid? folderId)
    {
        folderId = null;
        if (string.Equals(value, TopLevel, StringComparison.Ordinal)) return true;
        if (!Guid.TryParse(value, out var id)) return false;
        folderId = id;
        return true;
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

    // "Delete unused images": the confirmation page lists the unused images that match the
    // library search in the same folder (at most one run of them). With no folder and subfolders
    // included, that is the whole library. Nothing is deleted on a GET.
    [HttpGet("admin/media/delete-unused")]
    public async Task<IActionResult> DeleteUnusedConfirm(string? search, Guid? folder = null, bool subfolders = true, CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var tree = await folders.GetTreeAsync(ct);
        var shown = tree.Find(folder);
        if (folder is not null && shown is null) return NotFound();

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var unused = await media.ListAsync(new MediaAssetQuery(
            Search: term,
            UnusedOnly: true,
            Page: 1,
            PageSize: MediaDeletionService.MaxUnusedPerRun,
            Sort: MediaAssetSort.Name,
            FolderId: folder,
            IncludeSubfolders: subfolders), ct);
        return View(DeleteUnusedView, new MediaDeleteUnusedViewModel
        {
            Items = unused.Items.Select(a => MediaLibraryItemViewModel.From(a, urls) with { Folder = tree.PathText(a.FolderId) }).ToList(),
            TotalCount = unused.TotalCount,
            Search = term,
            Folder = shown,
            IncludeSubfolders = subfolders,
            BackUrl = UnusedListUrl(shown, subfolders, term),
        });
    }

    // Deletes only the images the confirmation page listed. The service checks each one's uses
    // again inside its delete, so an image that content started to use since then stays.
    [HttpPost("admin/media/delete-unused")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUnused(List<Guid> ids, string? search, Guid? folder = null, bool subfolders = true, CancellationToken ct = default)
    {
        var result = await deletions.DeleteUnusedAsync(ids ?? [], ct);
        TempData[SuccessKey] = DeleteUnusedMessage(result);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var shown = folder is Guid id ? new MediaFolderDto(id, "", null) : null;
        return Redirect(UnusedListUrl(shown, subfolders, term));
    }

    // The library list of unused images in the same folder, with the same search.
    private static string UnusedListUrl(MediaFolderDto? folder, bool subfolders, string? search) =>
        new MediaLibraryIndexViewModel
        {
            Page = new MediaAssetPage([], 0, 1, 1),
            Items = [],
            Folder = folder,
            IncludeSubfolders = subfolders,
            Search = search,
            Unused = true,
        }.PageUrl(1);

    private static string DeleteUnusedMessage(MediaUnusedDeleteResult result)
    {
        static string Images(int n) => n == 1 ? "1 unused image" : $"{n} unused images";
        var message = $"{Images(result.Deleted.Count)} deleted.";
        if (result.InUse.Count > 0)
        {
            message += result.InUse.Count == 1
                ? " 1 image was not deleted, because content now uses it."
                : $" {result.InUse.Count} images were not deleted, because content now uses them.";
        }
        return message;
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
            UsageBlocks = await media.GetUsageBlocksAsync(id, ct),
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
            UsageBlocks = await media.GetUsageBlocksAsync(id, ct),
        };
    }

    // Each file goes through the upload pipeline on its own: one bad file does not stop the rest.
    // A script call (Accept: application/json, token in the X-XSRF-TOKEN header) gets the results
    // as JSON. A plain form post gets the library page again, with the results above the grid.
    [HttpPost("admin/media/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    // New images go into "folder" (from the form's address), or to the top of the library.
    public async Task<IActionResult> Upload(List<IFormFile> files, Guid? folder = null, CancellationToken ct = default)
    {
        var chosen = (files ?? []).Where(f => !string.IsNullOrEmpty(f.FileName)).ToList();
        if (chosen.Count == 0)
        {
            if (WantsJson) return new JsonResult(new { error = NoFileError }) { StatusCode = StatusCodes.Status400BadRequest };
            Response.StatusCode = StatusCodes.Status400BadRequest;
            var empty = await BuildIndexAsync(new LibraryState(Folder: folder), ct) ?? await BuildIndexAsync(new LibraryState(), ct);
            empty!.UploadError = NoFileError;
            return View(IndexView, empty);
        }

        var results = new List<MediaUploadItemViewModel>();
        foreach (var file in chosen)
        {
            await using var stream = file.OpenReadStream();
            var result = await uploads.UploadAsync(stream, file.FileName, folder, ct);
            results.Add(ToItem(file.FileName, result));
        }

        if (WantsJson) return new JsonResult(results);

        var index = await BuildIndexAsync(new LibraryState(Folder: folder), ct) ?? await BuildIndexAsync(new LibraryState(), ct);
        index!.UploadResults = results;
        return View(IndexView, index);
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

    // What the library page shows: the query values of the list, all optional.
    private sealed record LibraryState(
        string? Search = null, bool MissingAlt = false, bool Unused = false, string? Sort = null, string? View = null,
        int Page = 1, Guid? Folder = null, bool Subfolders = true);

    // Null when the folder asked for does not exist.
    private async Task<MediaLibraryIndexViewModel?> BuildIndexAsync(LibraryState state, CancellationToken ct)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var tree = await folders.GetTreeAsync(ct);
        var folder = tree.Find(state.Folder);
        if (state.Folder is not null && folder is null) return null;

        var term = string.IsNullOrWhiteSpace(state.Search) ? null : state.Search.Trim();
        var byName = string.Equals(state.Sort, SortByName, StringComparison.OrdinalIgnoreCase);
        var result = await media.ListAsync(new MediaAssetQuery(
            Search: term,
            MissingAltText: state.MissingAlt,
            UnusedOnly: state.Unused,
            Page: Math.Max(1, state.Page),
            PageSize: await ResolvePageSizeAsync(),
            Sort: byName ? MediaAssetSort.Name : MediaAssetSort.Newest,
            FolderId: folder?.Id,
            IncludeSubfolders: state.Subfolders), ct);
        var uses = await media.CountUsesAsync(result.Items.Select(a => a.Id).ToList(), ct);

        return new MediaLibraryIndexViewModel
        {
            Page = result,
            Items = result.Items
                .Select(a => MediaLibraryItemViewModel.From(a, urls, uses.GetValueOrDefault(a.Id)) with { Folder = tree.PathText(a.FolderId) })
                .ToList(),
            Search = term,
            MissingAlt = state.MissingAlt,
            Unused = state.Unused,
            Sort = byName ? SortByName : SortNewest,
            View = MediaLibraryView.Normalise(state.View),
            Folders = tree,
            Folder = folder,
            IncludeSubfolders = state.Subfolders,
        };
    }

    private async Task<MediaFolderViewModel?> BuildFolderAsync(
        Guid id, string? name, string? nameError, string? moveError, string? deleteError, CancellationToken ct)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.Media;
        var tree = await folders.GetTreeAsync(ct);
        var folder = tree.Find(id);
        if (folder is null) return null;

        var (images, subfolders) = await folders.CountContentsAsync(id, ct);
        return new MediaFolderViewModel
        {
            Folder = folder,
            Folders = tree,
            ImageCount = images,
            SubfolderCount = subfolders,
            Name = name ?? folder.Name,
            NameError = nameError,
            MoveError = moveError,
            DeleteError = deleteError,
        };
    }

    private async Task<IActionResult> FolderErrorAsync(
        Guid id, string? name, string? nameError, string? moveError, string? deleteError, int status, CancellationToken ct)
    {
        var model = await BuildFolderAsync(id, name, nameError, moveError, deleteError, ct);
        if (model is null) return NotFound();
        Response.StatusCode = status;
        ViewData["HasError"] = true;
        return View(FolderView, model);
    }

    private async Task<int> ResolvePageSizeAsync()
    {
        var size = await settings.GetIntAsync(SettingKeys.CmsPageLength);
        return size > 0 ? size : DefaultPageSize;
    }
}
