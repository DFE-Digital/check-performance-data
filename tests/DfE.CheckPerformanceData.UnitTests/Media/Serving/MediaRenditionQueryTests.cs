using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;

// Reads the resize and crop settings from a media URL's query string. Anything that is not exactly
// one of the allowed shapes is refused, so a URL cannot ask for work the service never offers.
public sealed class MediaRenditionQueryTests
{
    private static MediaRenditionQueryResult Parse(params (string Key, string? Value)[] pairs) =>
        MediaRenditionQuery.Parse(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));

    [Fact]
    public void NoSettings_MeansTheOriginal()
    {
        var result = Parse();

        Assert.True(result.IsValid);
        Assert.Null(result.Rendition);
    }

    [Fact]
    public void ASignatureAlone_StillMeansTheOriginal()
    {
        var result = Parse(("sig", "abc"));

        Assert.True(result.IsValid);
        Assert.Null(result.Rendition);
    }

    [Theory]
    [InlineData("320")]
    [InlineData("640")]
    [InlineData("960")]
    [InlineData("1280")]
    [InlineData("1920")]
    public void AnAllowedWidth_IsRead(string width)
    {
        var result = Parse(("w", width), ("sig", "s"));

        Assert.True(result.IsValid);
        Assert.Equal(int.Parse(width), result.Rendition!.Width);
        Assert.Equal("s", result.Signature);
    }

    [Theory]
    [InlineData("500")]
    [InlineData("0")]
    [InlineData("-320")]
    [InlineData("3840")]
    [InlineData("640.0")]
    [InlineData(" 640")]
    [InlineData("0640")]
    [InlineData("abc")]
    [InlineData("")]
    public void AWidthOutsideTheFixedSet_IsRefused(string width)
    {
        Assert.False(Parse(("w", width), ("sig", "s")).IsValid);
    }

    [Fact]
    public void ARepeatedSetting_IsRefused()
    {
        Assert.False(Parse(("w", "320"), ("w", "640"), ("sig", "s")).IsValid);
        Assert.False(Parse(("w", "320"), ("sig", "a"), ("sig", "b")).IsValid);
    }

    [Fact]
    public void ACrop_IsRoundedToThreePlaces()
    {
        var result = Parse(("crop", "0.12345,0.2,0.5,0.4999"), ("sig", "s"));

        Assert.True(result.IsValid);
        Assert.Equal(new MediaCrop(0.123m, 0.2m, 0.5m, 0.5m), result.Rendition!.Crop);
        Assert.Equal("crop=0.123,0.2,0.5,0.5", MediaRenditionQuery.Canonical(result.Rendition));
    }

    [Theory]
    [InlineData("0.1,0.2,0.5")]
    [InlineData("0.1,0.2,0.5,0.5,0.1")]
    [InlineData("-0.1,0,0.5,0.5")]
    [InlineData("0,0,0,0.5")]
    [InlineData("0,0,0.5,0")]
    [InlineData("0.6,0,0.5,0.5")]
    [InlineData("0,0.6,0.5,0.5")]
    [InlineData("0,0,1.5,1")]
    [InlineData("a,0,0.5,0.5")]
    [InlineData("NaN,0,0.5,0.5")]
    [InlineData("1e-1,0,0.5,0.5")]
    public void ACropOutsideTheImage_IsRefused(string crop)
    {
        Assert.False(Parse(("crop", crop), ("sig", "s")).IsValid);
    }

    [Fact]
    public void ACropThatReachesTheEdgeAfterRounding_IsAllowed()
    {
        Assert.True(Parse(("crop", "0.3334,0,0.6667,1"), ("sig", "s")).IsValid);
    }

    [Fact]
    public void AnAspectAndFocalPoint_AreRead()
    {
        var result = Parse(("w", "640"), ("ar", "16:9"), ("focal", "0.25,0.75"), ("sig", "s"));

        Assert.True(result.IsValid);
        Assert.Equal(new MediaAspectRatio(16, 9), result.Rendition!.Aspect);
        Assert.Equal(new MediaFocalPoint(0.25m, 0.75m), result.Rendition.Focal);
    }

    [Theory]
    [InlineData("0:9")]
    [InlineData("16:0")]
    [InlineData("16")]
    [InlineData("16:9:1")]
    [InlineData("-16:9")]
    [InlineData("1.5:1")]
    [InlineData("1001:1")]
    public void ABadAspect_IsRefused(string aspect)
    {
        Assert.False(Parse(("ar", aspect), ("sig", "s")).IsValid);
    }

    [Fact]
    public void AFocalPointWithoutAnAspect_IsRefused()
    {
        Assert.False(Parse(("focal", "0.5,0.5"), ("sig", "s")).IsValid);
    }

    [Theory]
    [InlineData("1.1,0.5")]
    [InlineData("0.5")]
    [InlineData("-0.1,0.5")]
    public void AFocalPointOutsideTheImage_IsRefused(string focal)
    {
        Assert.False(Parse(("ar", "1:1"), ("focal", focal), ("sig", "s")).IsValid);
    }

    [Fact]
    public void TheCanonicalForm_HasAFixedOrder_WhateverTheQueryOrder()
    {
        var a = Parse(("focal", "0.5,0.5"), ("ar", "4:3"), ("crop", "0,0,1,1"), ("w", "960"), ("sig", "s"));
        var b = Parse(("sig", "s"), ("w", "960"), ("crop", "0.000,0,1.0,1"), ("ar", "4:3"), ("focal", ".5,.50"));

        Assert.Equal("w=960&crop=0,0,1,1&ar=4:3&focal=0.5,0.5", MediaRenditionQuery.Canonical(a.Rendition!));
        Assert.Equal(MediaRenditionQuery.Canonical(a.Rendition!), MediaRenditionQuery.Canonical(b.Rendition!));
    }

    [Fact]
    public void OtherQueryValues_AreIgnored()
    {
        var result = Parse(("w", "320"), ("utm_source", "x"), ("sig", "s"));

        Assert.True(result.IsValid);
        Assert.Equal(320, result.Rendition!.Width);
    }
}
