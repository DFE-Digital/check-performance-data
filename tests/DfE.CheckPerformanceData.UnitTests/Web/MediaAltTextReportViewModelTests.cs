using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Each row of the alt text report links to where the text can be fixed: the image's own page when
// readers get the library's text, otherwise the page or content block editor.
public sealed class MediaAltTextReportViewModelTests
{
    private static readonly Guid Image = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid Page = new("11111111-1111-4111-8111-111111111111");

    private static MediaAltTextUseRow Use(Guid? page, string? block, bool fromLibrary, params EmbeddedVersionState[] states) =>
        new(Image, "a.png", page, block, page is null ? block! : "Results", states, MediaUsageLocations.RichText, "image", fromLibrary, AltTextProblem.Generic);

    [Fact]
    public void AUseWithItsOwnText_OnAPage_LinksToThePageEditor()
    {
        var use = Use(Page, null, false, EmbeddedVersionState.Live);

        Assert.Equal($"/admin/pages/{Page}", MediaAltTextReportViewModel.FixUrl(use));
        Assert.Equal("Edit page", MediaAltTextReportViewModel.FixText(use));
    }

    [Fact]
    public void AUseWithItsOwnText_InABlock_LinksToTheBlockEditor_WithTheKeyEscaped()
    {
        var use = Use(null, "footer logo", false);

        Assert.Equal("/admin/content-blocks?edit=footer%20logo#edit-footer%20logo", MediaAltTextReportViewModel.FixUrl(use));
        Assert.Equal("Edit content block", MediaAltTextReportViewModel.FixText(use));
    }

    [Fact]
    public void AUseThatShowsTheLibraryText_LinksToTheImage()
    {
        var use = Use(Page, null, true, EmbeddedVersionState.Live);

        Assert.Equal($"/admin/media/{Image}", MediaAltTextReportViewModel.FixUrl(use));
        Assert.Equal("Edit image", MediaAltTextReportViewModel.FixText(use));
    }

    [Theory]
    [InlineData(new EmbeddedVersionState[0], "Content block")]
    [InlineData(new[] { EmbeddedVersionState.Live }, "Live")]
    [InlineData(new[] { EmbeddedVersionState.Live, EmbeddedVersionState.Draft }, "Live and draft")]
    [InlineData(new[] { EmbeddedVersionState.Live, EmbeddedVersionState.Scheduled, EmbeddedVersionState.Draft }, "Live, scheduled and draft")]
    public void TheVersionsColumn_NamesEachCurrentVersion(EmbeddedVersionState[] states, string expected)
    {
        Assert.Equal(expected, MediaAltTextReportViewModel.Versions(Use(states.Length == 0 ? null : Page, "block", false, states)));
    }

    [Fact]
    public void PageLinks_KeepTheOtherListsPage()
    {
        var model = new MediaAltTextReportViewModel(
            new MediaReportList<MediaAltTextAssetRow>([], 2, 3, 30),
            new MediaReportList<MediaAltTextUseRow>([], 4, 5, 50));

        Assert.Equal("/admin/media/reports/alt-text?imagesPage=3&usesPage=4#images", model.ImagesPageUrl(3));
        Assert.Equal("/admin/media/reports/alt-text?imagesPage=2&usesPage=1#uses", model.UsesPageUrl(1));
    }
}
