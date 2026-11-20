using DfE.CheckPerformanceData.Application.Media.Upload;
using SkiaSharp;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Upload;

// GIFs and animated WebPs are stored as uploaded, because re-encoding would lose the animation.
// So their metadata blocks are cut out byte by byte, and nothing after the end of the image
// survives. The frames must be untouched.
public sealed class AnimatedMetadataStripperTests
{
    [Fact]
    public void Gif_KeepsFramesAndLoop_AndDropsCommentsApplicationBlocksAndTrailingBytes()
    {
        var gif = ImageFixtures.Gif(4, 4, [0, 1, 0],
            comment: "SECRET-COMMENT", xmp: "SECRET-XMP", plainText: "SECRET-TEXT", afterTrailer: "<script>alert(1)</script>");

        var stripped = GifMetadataStripper.Strip(gif);

        Assert.False(ImageFixtures.Contains(stripped, "SECRET"));
        Assert.False(ImageFixtures.Contains(stripped, "XMP DataXMP"));
        Assert.False(ImageFixtures.Contains(stripped, "script"));
        Assert.True(ImageFixtures.Contains(stripped, "NETSCAPE2.0"));
        Assert.Equal(0x3B, stripped[^1]);

        using var codec = SKCodec.Create(new MemoryStream(stripped));
        Assert.NotNull(codec);
        Assert.Equal(3, codec.FrameCount);
        Assert.Equal(4, codec.Info.Width);
    }

    [Fact]
    public void Gif_WithNothingToStrip_IsUnchanged()
    {
        var gif = ImageFixtures.Gif(3, 2, [0, 1]);

        Assert.Equal(gif, GifMetadataStripper.Strip(gif));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(40)]
    public void Gif_ThatIsCutShort_IsRefused(int keep)
    {
        var gif = ImageFixtures.Gif(4, 4, [0, 1]);

        Assert.Throws<InvalidDataException>(() => GifMetadataStripper.Strip(gif[..keep]));
    }

    [Fact]
    public void WebP_KeepsFrames_AndDropsExifXmpIccAndTrailingBytes()
    {
        var webp = ImageFixtures.AnimatedWebP(4, 4, [ImageFixtures.Red, ImageFixtures.Blue],
            exif: "SECRET-EXIF", xmp: "SECRET-XMP", icc: "SECRET-ICC", afterRiff: "<script>alert(1)</script>");

        var stripped = WebPContainer.StripMetadata(webp);

        Assert.False(ImageFixtures.Contains(stripped, "SECRET"));
        Assert.False(ImageFixtures.Contains(stripped, "script"));
        Assert.Equal(["VP8X", "ANIM", "ANMF", "ANMF"], ImageFixtures.TopLevelChunkIds(stripped));
        var flags = ImageFixtures.TopLevelChunk(stripped, "VP8X")[0];
        Assert.Equal(ImageFixtures.VP8XAnimation, flags);
        Assert.Equal(stripped.Length - 8, BitConverter.ToInt32(stripped, 4));

        using var codec = SKCodec.Create(new MemoryStream(stripped));
        Assert.NotNull(codec);
        Assert.Equal(2, codec.FrameCount);
    }

    [Fact]
    public void WebP_ThatIsNotARiffFile_IsRefused()
    {
        Assert.Throws<InvalidDataException>(() => WebPContainer.StripMetadata("RIFF\u0004\0\0\0WEBX"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => WebPContainer.StripMetadata(ImageFixtures.Png()));
    }

    [Fact]
    public void WebP_TellsLosslessFromLossy()
    {
        Assert.True(WebPContainer.IsLossless(ImageFixtures.WebPLossless()));
        Assert.False(WebPContainer.IsLossless(ImageFixtures.WebPLossy()));
    }
}
