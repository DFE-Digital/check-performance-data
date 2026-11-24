using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Widget;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Widget;

// The Image widget's props are checked when an editor saves the widget. A failed check names the
// field and says what to do, and nothing is saved.
public sealed class ImageWidgetValidatorTests
{
    private static readonly Guid KnownId = Guid.Parse("3f1c4a52-38a9-4a65-9e3a-5f8f2f0f7a11");
    private readonly IMediaAssetRepository _assets = Substitute.For<IMediaAssetRepository>();
    private readonly ImageWidgetValidator _sut;

    public ImageWidgetValidatorTests()
    {
        var version = new MediaAssetVersionDto(KnownId, 1, "k", "image/png", 64, 32, 10, "sha", false, DateTime.UtcNow, "u");
        var asset = new MediaAssetDto(KnownId, "a.png", "a", null, null, null, [], 1, version, DateTime.UtcNow, "u", DateTime.UtcNow, "u");
        _assets.GetAsync(KnownId, Arg.Any<CancellationToken>()).Returns(asset);
        _sut = new ImageWidgetValidator(_assets);
    }

    private static JsonObject Props(Action<JsonObject>? change = null)
    {
        var props = WidgetRegistry.CreateDefaultProps("image");
        props["mediaId"] = KnownId.ToString();
        props["alt"] = "Pupils in a classroom";
        change?.Invoke(props);
        return props;
    }

    private async Task<IReadOnlyList<WidgetFieldError>> ErrorsAsync(Action<JsonObject> change) =>
        (await _sut.ValidateAsync(Props(change))).Errors;

    private static void AssertOnlyError(IReadOnlyList<WidgetFieldError> errors, string field, string message)
    {
        var error = Assert.Single(errors);
        Assert.Equal(field, error.Field);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void ValidatesTheImageWidget() => Assert.Equal("image", _sut.WidgetType);

    [Fact]
    public async Task CompleteProps_AreValid_AndSavedAsPosted()
    {
        var props = Props(p =>
        {
            p["caption"] = "Year 6";
            p["credit"] = "DfE";
            p["width"] = "one-half";
            p["align"] = "right";
            p["href"] = "https://www.gov.uk";
            p["crop"] = "0.1,0.2,0.5,0.5";
            p["aspect"] = "16:9";
            p["focal"] = "0.5,0.4";
        });

        var result = await _sut.ValidateAsync(props);

        Assert.True(result.IsValid);
        Assert.Equal(props.ToJsonString(), result.Props.ToJsonString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AMissingImage_IsAnError(string mediaId) =>
        AssertOnlyError(await ErrorsAsync(p => p["mediaId"] = mediaId), "mediaId", "Select an image");

    [Fact]
    public async Task AnImageThatIsNotInTheLibrary_IsAnError() =>
        AssertOnlyError(await ErrorsAsync(p => p["mediaId"] = Guid.NewGuid().ToString()),
            "mediaId", "Select an image that is in the media library");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoAltText_IsAnError_WhenTheImageIsNotDecorative(string alt) =>
        AssertOnlyError(await ErrorsAsync(p => p["alt"] = alt),
            "alt", "Enter alt text, or select that the image is decorative");

    [Fact]
    public async Task ADecorativeImage_NeedsNoAltText_AndSavesAnEmptyAlt()
    {
        var result = await _sut.ValidateAsync(Props(p =>
        {
            p["decorative"] = "true";
            p["alt"] = "Some text that a screen reader must not read";
        }));

        Assert.True(result.IsValid);
        Assert.Equal("", result.Props["alt"]!.GetValue<string>());
        Assert.Equal("true", result.Props["decorative"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    public async Task ADecorativeValueOtherThanTrue_SavesAsFalse(string value)
    {
        var result = await _sut.ValidateAsync(Props(p => p["decorative"] = value));

        Assert.True(result.IsValid);
        Assert.Equal("false", result.Props["decorative"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("full")]
    [InlineData("two-thirds")]
    [InlineData("one-half")]
    [InlineData("one-third")]
    public async Task EachGridWidth_IsValid(string width) =>
        Assert.Empty(await ErrorsAsync(p => p["width"] = width));

    [Theory]
    [InlineData("")]
    [InlineData("one-quarter")]
    [InlineData("FULL")]
    public async Task AnyOtherWidth_IsAnError(string width) =>
        AssertOnlyError(await ErrorsAsync(p => p["width"] = width), "width", "Select a width");

    [Theory]
    [InlineData("")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("centre")]
    public async Task EachAlignment_IsValid(string align) =>
        Assert.Empty(await ErrorsAsync(p => p["align"] = align));

    [Fact]
    public async Task AnyOtherAlignment_IsAnError() =>
        AssertOnlyError(await ErrorsAsync(p => p["align"] = "middle"), "align", "Select an alignment");

    [Theory]
    [InlineData("")]
    [InlineData("/guidance")]
    [InlineData("https://www.gov.uk")]
    public async Task ASafeLinkOrNoLink_IsValid(string href) =>
        Assert.Empty(await ErrorsAsync(p => p["href"] = href));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example.com")]
    [InlineData("mailto:a@b.c")]
    public async Task AnUnsafeLink_IsAnError(string href) =>
        AssertOnlyError(await ErrorsAsync(p => p["href"] = href),
            "href", "Enter a link that starts with https://, http:// or /");

    [Theory]
    [InlineData("")]
    [InlineData("0,0,1,1")]
    [InlineData("0.1,0.2,0.5,0.5")]
    public async Task ACropInsideTheImage_IsValid(string crop) =>
        Assert.Empty(await ErrorsAsync(p => p["crop"] = crop));

    [Theory]
    [InlineData("0.6,0,0.5,1")]
    [InlineData("0,0,0,1")]
    [InlineData("-0.1,0,0.5,0.5")]
    [InlineData("0.1,0.1,0.5")]
    [InlineData("a,b,c,d")]
    [InlineData("1.5,0,0.5,0.5")]
    public async Task ACropOutsideTheImageOrInAnotherShape_IsAnError(string crop) =>
        AssertOnlyError(await ErrorsAsync(p => p["crop"] = crop),
            "crop", "Enter the crop as x,y,width,height: 4 numbers from 0 to 1 that stay inside the image");

    [Theory]
    [InlineData("")]
    [InlineData("16:9")]
    [InlineData("4:3")]
    [InlineData("1:1")]
    [InlineData("3:2")]
    public async Task EachAspectRatio_IsValid(string aspect) =>
        Assert.Empty(await ErrorsAsync(p => p["aspect"] = aspect));

    [Theory]
    [InlineData("5:4")]
    [InlineData("16/9")]
    public async Task AnyOtherAspectRatio_IsAnError(string aspect) =>
        AssertOnlyError(await ErrorsAsync(p => p["aspect"] = aspect), "aspect", "Select an aspect ratio");

    [Theory]
    [InlineData("")]
    [InlineData("0,0")]
    [InlineData("0.5,1")]
    public async Task AFocalPointInsideTheImage_IsValid(string focal) =>
        Assert.Empty(await ErrorsAsync(p => p["focal"] = focal));

    [Theory]
    [InlineData("1.2,0")]
    [InlineData("0.5")]
    [InlineData("x,y")]
    public async Task AFocalPointOutsideTheImage_IsAnError(string focal) =>
        AssertOnlyError(await ErrorsAsync(p => p["focal"] = focal),
            "focal", "Enter the focal point as x,y: 2 numbers from 0 to 1");

    [Fact]
    public async Task EveryProblem_IsReported_InFieldOrder()
    {
        var errors = await ErrorsAsync(p =>
        {
            p["mediaId"] = "";
            p["alt"] = "";
            p["width"] = "wide";
            p["href"] = "javascript:x";
        });

        Assert.Equal(["mediaId", "alt", "width", "href"], errors.Select(e => e.Field));
    }

    [Fact]
    public async Task PropsWithMissingKeys_AreReadAsEmpty_AndDoNotThrow()
    {
        var result = await _sut.ValidateAsync(new JsonObject { ["mediaId"] = KnownId.ToString(), ["alt"] = "x" });

        Assert.Equal(["width"], result.Errors.Select(e => e.Field));
    }
}
