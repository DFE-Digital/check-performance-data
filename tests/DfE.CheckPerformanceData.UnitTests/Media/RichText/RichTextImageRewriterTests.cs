using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.RichText;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.RichText;

// Rich text stores a library image as <img data-media-id src="/media/{id}/..."> with the unsigned
// original. When the page renders, each one is pointed at the version the page shows, with signed
// responsive copies. Images that are not from the library are left exactly as stored.
public sealed class RichTextImageRewriterTests
{
    private static readonly Guid Id = Guid.Parse("8a8e3a52-38a9-4a65-9e3a-5f8f2f0f7a11");
    private static readonly Guid OtherId = Guid.Parse("1b2c3d4e-0000-4000-8000-000000000002");
    private readonly MediaUrlSigner _signer = new(new FixedSigningKey(Enumerable.Repeat((byte)7, 32).ToArray()));
    private readonly MediaUrlBuilder _urls;

    public RichTextImageRewriterTests()
    {
        _urls = new MediaUrlBuilder(_signer);
    }

    private static ResolvedMedia Media(int version = 2, string contentType = "image/png", int? width = 2000, int? height = 1000,
        bool animated = false, string? defaultAlt = "Library alt", Guid? id = null)
    {
        var assetId = id ?? Id;
        var v = new MediaAssetVersionDto(assetId, version, MediaBlobKeys.Original(assetId, version), contentType, width, height,
            100, "sha", animated, DateTime.UtcNow, "u");
        var asset = new MediaAssetDto(assetId, "Photo.png", "school-photo", defaultAlt, null, null, [], version, v,
            DateTime.UtcNow, "u", DateTime.UtcNow, "u");
        return new ResolvedMedia(asset, v);
    }

    private static Dictionary<Guid, ResolvedMedia> Found(params ResolvedMedia[] media) =>
        media.ToDictionary(m => m.Asset.Id);

    private static IElement Img(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        return Assert.Single(document.QuerySelectorAll("img"));
    }

    private const string Stored =
        "<p>Intro</p><p><img data-media-id=\"8a8e3a52-38a9-4a65-9e3a-5f8f2f0f7a11\" src=\"/media/8a8e3a52-38a9-4a65-9e3a-5f8f2f0f7a11/1/school-photo.png\" alt=\"Pupils reading\" width=\"500\" height=\"250\"></p>";

    [Fact]
    public void ALibraryImage_GetsSignedResponsiveCopies_OfTheVersionShown()
    {
        var html = RichTextImageRewriter.Rewrite(Stored, Found(Media(version: 2)), _urls, showEditorNotices: false);

        var img = Img(html);
        var src = img.GetAttribute("src")!;
        Assert.StartsWith($"/media/{Id:D}/2/school-photo.webp?", src);
        // The fallback src is the smallest fixed width that fills the 500 pixels the editor set.
        Assert.Contains("w=640", src);
        Assert.Contains("sig=", src);
        var srcset = img.GetAttribute("srcset")!.Split(", ");
        Assert.All(srcset, e => Assert.Matches($"^/media/{Id:D}/2/school-photo\\.webp\\?.*sig=.* \\d+w$", e));
        Assert.EndsWith(" 1920w", srcset[^1]); // the largest fixed width; the image is 2000 wide
        Assert.NotNull(img.GetAttribute("sizes"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
        Assert.Equal("async", img.GetAttribute("decoding"));
        Assert.Equal("Pupils reading", img.GetAttribute("alt"));
        Assert.Equal("500", img.GetAttribute("width"));
        Assert.Equal("250", img.GetAttribute("height"));
        Assert.Equal(Id.ToString(), img.GetAttribute("data-media-id"));
        Assert.StartsWith("<p>Intro</p>", html);
    }

    [Fact]
    public void ASizedImage_HintsItsOwnWidth_AndAnUnsizedOneTheContentColumn()
    {
        var sized = Img(RichTextImageRewriter.Rewrite(Stored, Found(Media()), _urls, false));
        Assert.Equal("(max-width: 500px) 100vw, 500px", sized.GetAttribute("sizes"));

        var unsized = Img(RichTextImageRewriter.Rewrite(
            $"<img data-media-id=\"{Id}\" src=\"/media/{Id}/1/a.png\" alt=\"x\">", Found(Media()), _urls, false));
        Assert.Equal(RichTextImageRewriter.ColumnSizes, unsized.GetAttribute("sizes"));
        Assert.Contains("w=960", unsized.GetAttribute("src"));
        // An image the editor did not size gets the size of the version shown, so the page does not move.
        Assert.Equal("2000", unsized.GetAttribute("width"));
        Assert.Equal("1000", unsized.GetAttribute("height"));
    }

    [Fact]
    public void AReplacementOfAnotherShape_KeepsTheWidthTheEditorSet_AndTakesItsOwnHeight()
    {
        // Stored at 500 x 250 (2:1); the current version is square.
        var img = Img(RichTextImageRewriter.Rewrite(Stored, Found(Media(version: 3, width: 800, height: 800)), _urls, false));

        Assert.Equal("500", img.GetAttribute("width"));
        Assert.Equal("500", img.GetAttribute("height"));
        Assert.StartsWith($"/media/{Id:D}/3/", img.GetAttribute("src"));
    }

    [Fact]
    public void AnImageFoundOnlyByItsAddress_IsRewrittenToo()
    {
        var img = Img(RichTextImageRewriter.Rewrite(
            $"<img src=\"/media/{Id}/1/school-photo.png\" alt=\"x\">", Found(Media()), _urls, false));

        Assert.StartsWith($"/media/{Id:D}/2/school-photo.webp?", img.GetAttribute("src"));
    }

    [Fact]
    public void AnImageWithNoAltAttribute_TakesTheLibraryAltText()
    {
        var img = Img(RichTextImageRewriter.Rewrite(
            $"<img data-media-id=\"{Id}\" src=\"/media/{Id}/1/a.png\">", Found(Media(defaultAlt: "From the library")), _urls, false));

        Assert.Equal("From the library", img.GetAttribute("alt"));
    }

    [Fact]
    public void AnEmptyAlt_StaysEmpty_BecauseTheEditorMarkedTheImageDecorative()
    {
        var img = Img(RichTextImageRewriter.Rewrite(
            $"<img data-media-id=\"{Id}\" src=\"/media/{Id}/1/a.png\" alt=\"\">", Found(Media()), _urls, false));

        Assert.Equal("", img.GetAttribute("alt"));
    }

    [Theory]
    [InlineData("image/svg+xml", false, "svg")]
    [InlineData("image/gif", true, "gif")]
    [InlineData("image/webp", true, "webp")]
    public void AnSvgOrAnAnimation_GetsTheOriginal_WithNoSrcset(string contentType, bool animated, string extension)
    {
        var img = Img(RichTextImageRewriter.Rewrite(Stored, Found(Media(contentType: contentType, animated: animated, width: 300, height: 150)), _urls, false));

        Assert.Equal($"/media/{Id:D}/2/school-photo.{extension}", img.GetAttribute("src"));
        Assert.False(img.HasAttribute("srcset"));
        Assert.False(img.HasAttribute("sizes"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
    }

    [Fact]
    public void AStoredSrcsetOrSizes_IsReplaced_NotTrusted()
    {
        var img = Img(RichTextImageRewriter.Rewrite(
            $"<img data-media-id=\"{Id}\" src=\"/media/{Id}/1/a.png\" srcset=\"/elsewhere.png 10w\" sizes=\"10px\" alt=\"x\">",
            Found(Media(contentType: "image/svg+xml", width: 10, height: 10)), _urls, false));

        Assert.False(img.HasAttribute("srcset"));
        Assert.False(img.HasAttribute("sizes"));
    }

    [Fact]
    public void ADeletedImage_IsDroppedFromAPublicPage()
    {
        var html = RichTextImageRewriter.Rewrite(Stored, Found(), _urls, showEditorNotices: false);

        Assert.Equal("<p>Intro</p><p></p>", html);
    }

    [Fact]
    public void ADeletedImage_ShowsAWarning_InTheEditorAndPreviews()
    {
        var html = RichTextImageRewriter.Rewrite(Stored, Found(), _urls, showEditorNotices: true);

        Assert.DoesNotContain("<img", html);
        var document = new HtmlParser().ParseDocument(html);
        var warning = Assert.Single(document.QuerySelectorAll(".govuk-warning-text"));
        // Inline elements only, so the warning can sit inside the paragraph that held the image.
        Assert.Equal("SPAN", warning.TagName);
        Assert.Equal("P", warning.ParentElement!.TagName);
        Assert.Contains("This image is no longer in the media library", warning.TextContent);
    }

    [Fact]
    public void ImagesNotFromTheLibrary_AreLeftExactlyAsStored()
    {
        const string html =
            "<p><img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"Inline\"><img src=\"https://example.org/a.png\" alt=\"Remote\"><img src=\"/images/logo.png\" alt=\"Local\"></p>";

        Assert.Equal(html, RichTextImageRewriter.Rewrite(html, Found(Media()), _urls, false));
    }

    [Fact]
    public void TextWithNoLibraryImage_IsReturnedUntouched()
    {
        const string html = "<p>Plain <strong>text</strong> &amp; <a href=\"/x\">a link</a></p>";

        Assert.Same(html, RichTextImageRewriter.Rewrite(html, Found(Media()), _urls, false));
    }

    [Fact]
    public void FindIds_ListsEachLibraryImage_DataAttributeFirst()
    {
        var html = $"<img data-media-id=\"{Id}\" src=\"/media/{OtherId}/1/a.png\"><img src=\"/media/{OtherId}/1/b.png\"><img src=\"/x.png\">";

        Assert.Equal([Id, OtherId], RichTextImageRewriter.FindIds(html));
    }

    [Fact]
    public async Task RenderAsync_ResolvesThePageVersionsImages_ThenRewrites()
    {
        var resolver = Substitute.For<IMediaVersionResolver>();
        var pageVersion = Guid.NewGuid();
        resolver.ResolveAsync(Arg.Any<IEnumerable<Guid>>(), pageVersion, Arg.Any<CancellationToken>())
            .Returns(Found(Media(version: 1)));

        var html = await resolver.RenderRichTextImagesAsync(Stored, _urls, pageVersion, showEditorNotices: false);

        Assert.StartsWith($"/media/{Id:D}/1/", Img(html).GetAttribute("src"));
        await resolver.Received(1).ResolveAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { Id })), pageVersion, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenderAsync_SkipsTheLookup_WhenThereAreNoLibraryImages()
    {
        var resolver = Substitute.For<IMediaVersionResolver>();

        var html = await resolver.RenderRichTextImagesAsync("<p>Hi</p>", _urls, null, false);

        Assert.Equal("<p>Hi</p>", html);
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, default);
    }
}
