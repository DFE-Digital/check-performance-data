using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Source checks for the media picker dialog, the page editor that loads it, and the scripts and
// vendored crop library behind them. The dialog must be an accessible modal: a native <dialog>
// named by its heading, with a labelled search box and file control, a close button with a name,
// and its own antiforgery token for uploads. The scripts are files, so the CSP needs no change.
public sealed class MediaPickerViewSourceTests
{
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string WebRoot() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", "src", "DfE.CheckPerformanceData.Web"));

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([WebRoot(), .. segments]));

    private static string Dialog() => Read("Views", "Shared", "ContentPages", "_MediaPicker.cshtml");

    [Fact]
    public void TheDialog_IsANativeDialog_NamedByItsHeading()
    {
        var src = Dialog();

        Assert.Matches("<dialog[^>]*aria-labelledby=\"media-picker-title\"", src);
        Assert.Contains("id=\"media-picker-title\"", src);
        Assert.Contains(">Choose an image</h2>", src);
        Assert.Matches("<button[^>]*aria-label=\"Close\"[^>]*data-media-picker-close", src);
    }

    [Fact]
    public void TheDialog_LabelsItsSearchBoxAndFileControl()
    {
        var src = Dialog();

        Assert.Contains("<label class=\"govuk-label\" for=\"media-picker-search\">", src);
        Assert.Contains("id=\"media-picker-search\"", src);
        Assert.Contains("<label class=\"govuk-label\" for=\"media-picker-file\">", src);
        Assert.Matches("<input class=\"govuk-file-upload\" id=\"media-picker-file\"[^>]*type=\"file\"", src);
        Assert.Matches("role=\"status\"[^>]*data-media-picker-status", src);
    }

    [Fact]
    public void TheDialog_CarriesItsOwnAntiforgeryToken_AndItsEndpoints()
    {
        var src = Dialog();

        Assert.Contains("@Html.AntiForgeryToken()", src);
        Assert.Contains("data-list-url=\"/admin/media/picker\"", src);
        Assert.Contains("data-upload-url=\"/admin/media/picker/upload\"", src);
        Assert.Contains("data-max-bytes=\"@MediaUploadLimits.MaxFileBytes\"", src);
    }

    // The picker and its upload need the media section. The dialog checks the grant itself, so
    // no editor page can show it to someone who would get 404 from its endpoints.
    [Fact]
    public void TheDialog_RendersOnlyForPeopleWithTheMediaSection()
    {
        var src = Dialog();

        Assert.Contains("@inject IAdminAccessPolicy AccessPolicy", src);
        var guard = src.IndexOf("await AccessPolicy.CanAccessAsync(User, AdminNavKeys.Media)", StringComparison.Ordinal);
        Assert.True(guard > 0, "The dialog must check the media section.");
        Assert.True(src.IndexOf("<dialog", StringComparison.Ordinal) > guard, "The check must come before the dialog.");
    }

    [Fact]
    public void TheDialog_HasNoInlineHandlers()
    {
        Assert.DoesNotMatch("\\son[a-z]+=\"", Dialog());
    }

    [Fact]
    public void ThePageEditor_RendersTheDialogOnce_AndLoadsTheScriptsAsFiles()
    {
        var src = Read("Views", "ContentPage", "Edit.cshtml");

        Assert.Single(Regex.Matches(src, Regex.Escape("PartialAsync(\"~/Views/Shared/ContentPages/_MediaPicker.cshtml\")")));
        Assert.Contains("<link rel=\"stylesheet\" href=\"~/lib/cropperjs/cropper.min.css\"", src);
        Assert.Contains("<script src=\"~/lib/cropperjs/cropper.min.js\"></script>", src);
        Assert.Contains("<script src=\"~/js/media-picker.js\" asp-append-version=\"true\"></script>", src);
        Assert.Contains("<script src=\"~/js/image-widget-editor.js\" asp-append-version=\"true\"></script>", src);
    }

    [Fact]
    public void TheMediaPickerScript_DocumentsItsPublicApi_AndSendsTheTokenInAHeader()
    {
        var src = Read("wwwroot", "js", "media-picker.js");

        Assert.Contains("MediaPicker.open(", src);
        Assert.Contains("window.MediaPicker =", src);
        Assert.Contains("'X-XSRF-TOKEN'", src);
        Assert.Contains("showModal", src);
    }

    [Fact]
    public void TheScripts_SetTextFromTheServer_WithTextContentOnly()
    {
        foreach (var file in new[] { "media-picker.js", "image-widget-editor.js" })
        {
            var src = Read("wwwroot", "js", file);
            // The only HTML the scripts insert is the server-rendered preview of the widget.
            var inner = Regex.Matches(src, "innerHTML\\s*=").Count;
            Assert.True(inner <= (file == "image-widget-editor.js" ? 1 : 0), $"{file} sets innerHTML {inner} times");
        }
    }

    [Fact]
    public void Cropper_IsVendoredAtVersion162_WithItsMitLicence()
    {
        Assert.StartsWith("/*!\n * Cropper.js v1.6.2", Read("wwwroot", "lib", "cropperjs", "cropper.min.js").Replace("\r\n", "\n"));
        Assert.Contains("Cropper.js v1.6.2", Read("wwwroot", "lib", "cropperjs", "cropper.min.css"));
        Assert.Contains("The MIT License", Read("wwwroot", "lib", "cropperjs", "LICENSE"));
    }

    // The folder tree: buttons in nested lists, named by a heading, with one Tab stop (the chosen
    // folder), and a labelled "subfolders" box. The script lists and uploads into the chosen folder.
    [Fact]
    public void TheDialog_HasAFolderTree_AndASubfoldersBox()
    {
        var src = Dialog();

        Assert.Contains("role=\"group\" aria-labelledby=\"media-picker-folders-title\"", src);
        Assert.Contains("id=\"media-picker-folders-title\"", src);
        Assert.Matches("<button type=\"button\" class=\"app-media-picker__folder\" tabindex=\"0\" aria-pressed=\"true\"\\s+data-media-picker-folder=\"\">Media library</button>", src);
        Assert.Contains("tabindex=\"-1\" aria-pressed=\"false\"", src);
        Assert.Contains("<label class=\"govuk-label govuk-checkboxes__label\" for=\"media-picker-subfolders\">", src);
        Assert.Contains("id=\"media-picker-subfolders\" type=\"checkbox\" checked", src);
    }

    [Fact]
    public void TheScript_ListsAndUploadsIntoTheChosenFolder()
    {
        var js = Read("wwwroot", "js", "media-picker.js");

        Assert.Contains("url += '&folder=' + encodeURIComponent(state.folder);", js);
        Assert.Contains("url += '&subfolders=false';", js);
        Assert.Contains("body.append('folder', folder);", js);
        Assert.Contains("upload(file, state.folder)", js);
        Assert.Contains("event.key === 'ArrowDown'", js);
    }
}
