using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Reports;

// Builds the accessibility report from the library and the content that uses it. Library images
// are checked on their default alt text. Each use is checked on the text readers get: its own, or
// the library's when it gives none. Decorative uses need no text and are left out.
public sealed class MediaAltTextReportBuilderTests
{
    private static readonly Guid A = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid B = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid Page1 = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Page2 = new("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime T0 = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private static MediaReportAsset Asset(Guid id, string? alt, string fileName = "school.png", string? caption = null, int minutes = 0) =>
        new(id, fileName, alt, caption, T0.AddMinutes(minutes));

    private static string Image(Guid id, string alt, string decorative = "false", string caption = "") =>
        new JsonArray(new JsonObject
        {
            ["kind"] = "widget",
            ["type"] = "image",
            ["props"] = new JsonObject { ["mediaId"] = id.ToString(), ["alt"] = alt, ["decorative"] = decorative, ["caption"] = caption },
        }).ToJsonString();

    private static MediaReportPageVersion Version(Guid page, string content, EmbeddedVersionState state = EmbeddedVersionState.Live, string title = "Page", int minutes = 0) =>
        new(page, title, state, T0.AddMinutes(minutes), content);

    [Fact]
    public void LibraryImages_WithMissingOrWeakDefaultText_AreListed_NewestChangeFirst()
    {
        var good = Guid.NewGuid();
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, null, minutes: 1), Asset(B, "photo", minutes: 5), Asset(good, "A bar chart of results")],
            [], []);

        Assert.Equal(
        [
            new MediaAltTextAssetRow(B, "school.png", "photo", AltTextProblem.Generic),
            new MediaAltTextAssetRow(A, "school.png", null, AltTextProblem.Missing),
        ], report.Assets);
    }

    [Fact]
    public void LibraryText_ThatMatchesItsFileNameOrCaption_IsListed()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, "school-front", "school-front.jpg"), Asset(B, "Our new library", caption: "Our new library")],
            [], []);

        Assert.Equal(AltTextProblem.FileName, report.Assets.Single(r => r.AssetId == A).Problem);
        Assert.Equal(AltTextProblem.SameAsCaption, report.Assets.Single(r => r.AssetId == B).Problem);
    }

    [Fact]
    public void AUse_WithItsOwnWeakText_IsListed_ToFixInThePage()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, "A bar chart of results")],
            [Version(Page1, Image(A, "image"), title: "Results")], []);

        var row = Assert.Single(report.Uses);
        Assert.Equal(new MediaAltTextUseRow(A, "school.png", Page1, null, "Results", row.States,
            MediaUsageLocations.Widget, "image", false, AltTextProblem.Generic), row);
        Assert.Equal([EmbeddedVersionState.Live], row.States);
    }

    [Fact]
    public void AUse_WithNoTextOfItsOwn_IsCheckedOnTheLibraryText_AndSaysSo()
    {
        var html = new JsonArray(new JsonObject
        {
            ["kind"] = "widget",
            ["type"] = "richtext",
            ["props"] = new JsonObject { ["html"] = $"<img data-media-id=\"{A}\">" },
        }).ToJsonString();

        var report = MediaAltTextReportBuilder.Build([Asset(A, "logo")], [Version(Page1, html)], []);

        var row = Assert.Single(report.Uses);
        Assert.True(row.FromLibrary);
        Assert.Equal("logo", row.Alt);
        Assert.Equal(AltTextProblem.Generic, row.Problem);
        Assert.Equal(MediaUsageLocations.RichText, row.Location);
    }

    [Fact]
    public void AUse_WithGoodTextOfItsOwn_IsNotListed_EvenWhenTheLibraryTextIsWeak()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, null)], [Version(Page1, Image(A, "Pupils reading in the school library"))], []);

        Assert.Empty(report.Uses);
        Assert.Single(report.Assets);
    }

    [Fact]
    public void DecorativeUses_AreNotListed()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, null)],
            [Version(Page1, Image(A, "", decorative: "true"))],
            [new MediaReportBlock(7, "footer", T0, $"<img data-media-id=\"{A}\" alt=\"\" role=\"presentation\">")]);

        Assert.Empty(report.Uses);
    }

    [Fact]
    public void AUse_WithTheSameTextAsItsCaption_IsListed()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, "Something else entirely")],
            [Version(Page1, Image(A, "Sports day 2026", caption: "Sports day 2026"))], []);

        Assert.Equal(AltTextProblem.SameAsCaption, Assert.Single(report.Uses).Problem);
    }

    [Fact]
    public void TheSameUse_InTheLiveVersionAndTheDraft_IsOneRow_NamingBothStates()
    {
        var content = Image(A, "photo");
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, "A bar chart of results")],
            [Version(Page1, content, EmbeddedVersionState.Live), Version(Page1, content, EmbeddedVersionState.Draft, minutes: 3)], []);

        var row = Assert.Single(report.Uses);
        Assert.Equal([EmbeddedVersionState.Live, EmbeddedVersionState.Draft], row.States);
    }

    [Fact]
    public void ADraftThatFixesTheText_LeavesOnlyTheLiveVersionListed()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, "A bar chart of results")],
            [Version(Page1, Image(A, "photo")), Version(Page1, Image(A, "Results by year, as a bar chart"), EmbeddedVersionState.Draft)], []);

        Assert.Equal([EmbeddedVersionState.Live], Assert.Single(report.Uses).States);
    }

    [Fact]
    public void ContentBlocks_AreCheckedAsRichText_AndNameTheirKey()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, "A bar chart of results")], [],
            [new MediaReportBlock(7, "footer-logo", T0, $"<img data-media-id=\"{A}\" alt=\"school.png\">")]);

        var row = Assert.Single(report.Uses);
        Assert.Equal("footer-logo", row.BlockKey);
        Assert.Equal("footer-logo", row.OwnerName);
        Assert.Null(row.PageNodeId);
        Assert.Empty(row.States);
        Assert.Equal(AltTextProblem.FileName, row.Problem);
    }

    [Fact]
    public void UsesOfImagesThatAreNotInTheLibrary_AreLeftOut()
    {
        var report = MediaAltTextReportBuilder.Build([Asset(A, "Good text here")], [Version(Page1, Image(B, "image"))], []);

        Assert.Empty(report.Uses);
    }

    [Fact]
    public void Uses_PutTheMostRecentlyChangedPageOrBlockFirst()
    {
        var report = MediaAltTextReportBuilder.Build(
            [Asset(A, "Good text here")],
            [Version(Page1, Image(A, "image"), title: "Older", minutes: 1), Version(Page2, Image(A, "image"), title: "Newer", minutes: 9)],
            [new MediaReportBlock(7, "block", T0.AddMinutes(5), $"<img data-media-id=\"{A}\" alt=\"pic\">")]);

        Assert.Equal(["Newer", "block", "Older"], report.Uses.Select(u => u.OwnerName));
    }
}
