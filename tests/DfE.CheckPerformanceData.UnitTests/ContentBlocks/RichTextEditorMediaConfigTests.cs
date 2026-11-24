namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

// Every rich-text editor (content page widgets, wiki pages and both content block editors) starts
// TinyMCE through RichTextMedia.init, so the media library settings live in one place and the
// editors cannot drift: the library picker, uploads to the library, addresses kept as written and
// existing data: images handed back. No editor reads a chosen file into a data: URI any more.
// Rendered behaviour is covered by the E2E tests.
public sealed class RichTextEditorMediaConfigTests
{
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string Web(params string[] segments) =>
        File.ReadAllText(Path.Combine([Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..")),
            "src", "DfE.CheckPerformanceData.Web", .. segments]));

    public static TheoryData<string[]> Editors => new()
    {
        new[] { "Views", "ContentPage", "Edit.cshtml" },
        new[] { "Views", "PageTreeAdmin", "WikiEdit.cshtml" },
        new[] { "Views", "ContentBlock", "Index.cshtml" },
        new[] { "Views", "Shared", "Components", "EditableContent", "Default.cshtml" },
    };

    [Theory]
    [MemberData(nameof(Editors))]
    public void EachEditor_StartsThroughTheSharedMediaSetup(string[] view)
    {
        var src = Web(view);

        Assert.Contains("RichTextMedia.init(", src);
        Assert.DoesNotContain("tinymce.init(", src);
        Assert.DoesNotContain("readAsDataURL", src);
        Assert.Contains("<script src=\"~/js/rich-text-media.js\" asp-append-version=\"true\"></script>", src);
        Assert.Contains("<script src=\"~/js/media-picker.js\" asp-append-version=\"true\"></script>", src);
        Assert.Contains("@await Html.PartialAsync(\"~/Views/Shared/ContentPages/_MediaPicker.cshtml\")", src);
    }

    // The picker and its upload need the media section, so the block editors show the picker
    // only to people who have it.
    [Theory]
    [InlineData("Views", "ContentBlock", "Index.cshtml")]
    [InlineData("Views", "Shared", "Components", "EditableContent", "Default.cshtml")]
    public void TheBlockEditors_RenderThePicker_OnlyForPeopleWhoEditPages(params string[] view)
    {
        var src = Web(view);
        var guard = src.IndexOf("CanUseMediaLibrary)", StringComparison.Ordinal);

        Assert.True(guard > 0);
        Assert.True(src.IndexOf("_MediaPicker.cshtml", StringComparison.Ordinal) > guard);
    }

    [Fact]
    public void TheSharedSetup_AddsTheImagePlugin_KeepsAddresses_AndTakesNoPastedCopyWithoutTheLibrary()
    {
        var js = Web("wwwroot", "js", "rich-text-media.js");

        Assert.Contains("function init(textarea, config)", js);
        Assert.Contains("window.RichTextMedia = { options: options, setup: setup, init: init };", js);
        Assert.Contains("convert_urls: false", js);
        Assert.Contains("paste_data_images: !!library", js);
    }
}
