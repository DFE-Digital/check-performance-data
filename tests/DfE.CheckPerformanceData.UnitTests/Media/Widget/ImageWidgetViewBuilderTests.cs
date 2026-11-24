using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;
using Microsoft.AspNetCore.WebUtilities;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Widget;

// What the Image widget writes for a stored set of props and the image version the page shows.
// Stored props can come from an import that skipped the save checks, so a bad value is left out
// and never stops the page from rendering.
public sealed class ImageWidgetViewBuilderTests
{
    private static readonly Guid Id = Guid.Parse("8a8e3a52-38a9-4a65-9e3a-5f8f2f0f7a11");
    private readonly MediaUrlSigner _signer = new(new FixedSigningKey(Enumerable.Repeat((byte)7, 32).ToArray()));
    private readonly MediaUrlBuilder _urls;

    public ImageWidgetViewBuilderTests()
    {
        _urls = new MediaUrlBuilder(_signer);
    }

    private static ResolvedMedia Media(string contentType = "image/png", int? width = 2000, int? height = 1000,
        bool animated = false, string? defaultAlt = null)
    {
        var version = new MediaAssetVersionDto(Id, 2, MediaBlobKeys.Original(Id, 2), contentType, width, height, 100, "sha",
            animated, DateTime.UtcNow, "u");
        var asset = new MediaAssetDto(Id, "Photo.png", "school-photo", defaultAlt, "Library caption", "Library credit", [], 2,
            version, DateTime.UtcNow, "u", DateTime.UtcNow, "u");
        return new ResolvedMedia(asset, version);
    }

    private static WidgetNode Widget(Action<JsonObject>? change = null)
    {
        var props = WidgetRegistry.CreateDefaultProps("image");
        props["mediaId"] = Id.ToString();
        props["alt"] = "Pupils in a classroom";
        change?.Invoke(props);
        return new WidgetNode { Type = "image", Props = props };
    }

    private ImageWidgetView Build(WidgetNode widget, ResolvedMedia media) =>
        ImageWidgetViewBuilder.Build(widget, media, _urls);

    private static Dictionary<string, string> Query(string url) =>
        QueryHelpers.ParseQuery(url[url.IndexOf('?')..]).ToDictionary(p => p.Key, p => p.Value.ToString());

    [Fact]
    public void APng_WithACropAndAnAspectRatio_UsesSignedRenditions_SizedToTheShapeShown()
    {
        // The crop keeps the left half (1000 x 1000); 16:9 then cuts 1000 x 563 from it.
        var view = Build(Widget(p => { p["crop"] = "0,0,0.5,1"; p["aspect"] = "16:9"; }), Media());

        Assert.StartsWith($"/media/{Id:D}/2/school-photo.webp?", view.Src);
        var query = Query(view.Src);
        Assert.Equal("960", query["w"]);
        Assert.Equal("0,0,0.5,1", query["crop"]);
        Assert.Equal("16:9", query["ar"]);
        Assert.True(_signer.IsValid(Id, 2, MediaRenditionQuery.Normalise(960, new MediaCrop(0, 0, 0.5m, 1), new MediaAspectRatio(16, 9), null), query["sig"]));

        Assert.NotNull(view.SrcSet);
        var entries = view.SrcSet!.Split(", ");
        Assert.All(entries, e => Assert.Contains("sig=", e));
        Assert.EndsWith(" 1000w", entries[^1]);
        Assert.Equal(1000, view.Width);
        Assert.Equal(563, view.Height);
        Assert.NotNull(view.Sizes);
    }

    [Fact]
    public void APng_WithAnAspectRatioAndAFocalPoint_PassesBothToTheRendition()
    {
        var view = Build(Widget(p => { p["aspect"] = "1:1"; p["focal"] = "0.25,0.5"; }), Media());

        var query = Query(view.Src);
        Assert.Equal("1:1", query["ar"]);
        Assert.Equal("0.25,0.5", query["focal"]);
        Assert.Equal(1000, view.Width);
        Assert.Equal(1000, view.Height);
    }

    [Fact]
    public void APng_WithNoCrop_KeepsItsOwnShape()
    {
        var view = Build(Widget(), Media());

        Assert.Equal(2000, view.Width);
        Assert.Equal(1000, view.Height);
        Assert.Equal("960", Query(view.Src)["w"]);
    }

    [Theory]
    [InlineData("image/svg+xml", false)]
    [InlineData("image/gif", true)]
    [InlineData("image/webp", true)]
    public void AnSvgOrAnAnimation_UsesTheOriginal_WithNoSrcset_AndIgnoresTheCrop(string contentType, bool animated)
    {
        var view = Build(Widget(p => { p["crop"] = "0,0,0.5,1"; p["aspect"] = "16:9"; }),
            Media(contentType, 64, 32, animated));

        Assert.DoesNotContain("?", view.Src);
        Assert.StartsWith($"/media/{Id:D}/2/school-photo.", view.Src);
        Assert.DoesNotContain(".webp", contentType == "image/webp" ? "" : view.Src);
        Assert.Null(view.SrcSet);
        Assert.Null(view.Sizes);
        Assert.Equal(64, view.Width);
        Assert.Equal(32, view.Height);
    }

    [Fact]
    public void AnSvgWithNoKnownSize_HasNoWidthOrHeight()
    {
        var view = Build(Widget(), Media("image/svg+xml", null, null));

        Assert.Null(view.Width);
        Assert.Null(view.Height);
    }

    [Fact]
    public void ADecorativeImage_HasAnEmptyAlt_EvenWhenAltTextIsStored()
    {
        var view = Build(Widget(p => { p["decorative"] = "true"; p["alt"] = "Leftover text"; }), Media(defaultAlt: "Library alt"));

        Assert.Equal("", view.Alt);
    }

    [Fact]
    public void TheWidgetAltText_IsUsed()
    {
        var view = Build(Widget(), Media(defaultAlt: "Library alt"));

        Assert.Equal("Pupils in a classroom", view.Alt);
    }

    [Fact]
    public void NoWidgetAltText_FallsBackToTheLibraryAltText()
    {
        var view = Build(Widget(p => p["alt"] = ""), Media(defaultAlt: "Library alt"));

        Assert.Equal("Library alt", view.Alt);
    }

    [Theory]
    [InlineData("/guidance", "/guidance")]
    [InlineData("https://www.gov.uk", "https://www.gov.uk")]
    [InlineData("", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("//evil.example.com", null)]
    public void ALink_IsKeptOnlyWhenItIsSafe(string href, string? expected)
    {
        var view = Build(Widget(p => p["href"] = href), Media());

        Assert.Equal(expected, view.Href);
    }

    [Fact]
    public void TheCaptionAndCredit_ComeFromTheWidget()
    {
        var view = Build(Widget(p => { p["caption"] = "Year 6 maths"; p["credit"] = "Photo: DfE"; }), Media());

        Assert.Equal("Year 6 maths", view.Caption);
        Assert.Equal("Photo: DfE", view.Credit);
    }

    [Fact]
    public void NoCaptionOrCredit_IsNull()
    {
        var view = Build(Widget(p => { p["caption"] = "  "; p["credit"] = ""; }), Media());

        Assert.Null(view.Caption);
        Assert.Null(view.Credit);
    }

    [Theory]
    [InlineData("full", "", "app-image app-image--full", "960")]
    [InlineData("two-thirds", "left", "app-image app-image--two-thirds app-image--align-left", "640")]
    [InlineData("one-half", "right", "app-image app-image--one-half app-image--align-right", "640")]
    [InlineData("one-third", "centre", "app-image app-image--one-third app-image--align-centre", "320")]
    [InlineData("enormous", "sideways", "app-image app-image--full", "960")]
    public void TheWidthAndAlignment_SetTheClasses_AndTheDefaultWidth(string width, string align, string css, string w)
    {
        var view = Build(Widget(p => { p["width"] = width; p["align"] = align; }), Media());

        Assert.Equal(css, view.CssClass);
        Assert.Equal(w, Query(view.Src)["w"]);
    }

    [Theory]
    [InlineData("full", "(min-width: 1020px) 960px, 100vw")]
    [InlineData("two-thirds", "(min-width: 1020px) 640px, (min-width: 641px) 67vw, 100vw")]
    [InlineData("one-half", "(min-width: 1020px) 480px, (min-width: 641px) 50vw, 100vw")]
    [InlineData("one-third", "(min-width: 1020px) 320px, (min-width: 641px) 34vw, 100vw")]
    public void Sizes_FollowTheGovukGridWidth(string width, string sizes)
    {
        var view = Build(Widget(p => p["width"] = width), Media());

        Assert.Equal(sizes, view.Sizes);
    }

    [Theory]
    [InlineData("crop", "0.6,0,0.5,1")]
    [InlineData("crop", "nonsense")]
    [InlineData("aspect", "0:9")]
    [InlineData("aspect", "5:4")]
    [InlineData("focal", "2,2")]
    public void ABadStoredValue_IsLeftOut_AndDoesNotThrow(string prop, string value)
    {
        var view = Build(Widget(p => p[prop] = value), Media());

        var query = Query(view.Src);
        Assert.False(query.ContainsKey(prop == "aspect" ? "ar" : prop));
        Assert.Equal(2000, view.Width);
    }

    [Fact]
    public void AFocalPointWithNoAspectRatio_IsLeftOut()
    {
        var view = Build(Widget(p => p["focal"] = "0.5,0.5"), Media());

        Assert.False(Query(view.Src).ContainsKey("focal"));
    }

    [Fact]
    public void PropsStoredWithMissingKeys_StillRender()
    {
        var widget = new WidgetNode { Type = "image", Props = new JsonObject { ["mediaId"] = Id.ToString() } };

        var view = Build(widget, Media(defaultAlt: "Library alt"));

        Assert.Equal("app-image app-image--full", view.CssClass);
        Assert.Equal("Library alt", view.Alt);
    }
}
