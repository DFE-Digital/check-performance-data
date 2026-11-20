using System.Text;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Infrastructure.Media;
using SkiaSharp;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Upload;

// The whole check-and-clean step an upload goes through, with the real Skia codecs: type from the
// bytes, size and dimension limits before decoding, EXIF orientation applied, metadata stripped.
public sealed class MediaFileCleanerTests
{
    private readonly MediaFileCleaner _sut = new(new SkiaRasterImageCleaner());

    private Task<MediaCheckResult> CleanAsync(byte[] bytes) => _sut.CleanAsync(new MemoryStream(bytes));

    private async Task<CleanedMediaFile> AcceptedAsync(byte[] bytes)
    {
        var result = await CleanAsync(bytes);
        Assert.True(result.IsAccepted, result.Rejection);
        return result.File!;
    }

    // Proves the native Skia library loads on this platform, by encoding and decoding an image.
    [Fact]
    public void SkiaNativeLibrary_LoadsAndEncodes()
    {
        var png = ImageFixtures.Png(2, 2);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(ImageFixtures.Red, bitmap.GetPixel(1, 1));
    }

    [Fact]
    public async Task Png_IsReEncodedAsPng_WithItsSize()
    {
        var file = await AcceptedAsync(ImageFixtures.Png(8, 6));

        Assert.Equal(MediaFormat.Png, file.Format);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal((8, 6), (file.Width, file.Height));
        Assert.False(file.IsAnimated);
        Assert.Equal(MediaFormat.Png, MediaTypeSniffer.Sniff(file.Bytes));
    }

    [Fact]
    public async Task Jpeg_LosesExifGpsXmpAndComments()
    {
        var jpeg = ImageFixtures.WithSegments(ImageFixtures.Jpeg(16, 8),
            ImageFixtures.ExifSegment(1, "SECRET-DESCRIPTION", "SECRET-GPS-AREA"),
            ImageFixtures.XmpSegment("<exif:GPSLatitude>51,30N</exif:GPSLatitude>"),
            ImageFixtures.CommentSegment("SECRET-COMMENT"));
        // The fixture really carries what the test claims: the planted text is in the upload.
        Assert.True(ImageFixtures.Contains(jpeg, "SECRET-GPS-AREA"));
        Assert.True(ImageFixtures.Contains(jpeg, "Exif\0\0"));

        var file = await AcceptedAsync(jpeg);

        Assert.Equal(MediaFormat.Jpeg, file.Format);
        Assert.False(ImageFixtures.Contains(file.Bytes, "Exif\0\0"));
        Assert.False(ImageFixtures.Contains(file.Bytes, "SECRET"));
        Assert.False(ImageFixtures.Contains(file.Bytes, "GPS"));
        Assert.False(ImageFixtures.Contains(file.Bytes, "http://ns.adobe.com/xap"));
        Assert.False(ImageFixtures.Contains(file.Bytes, "ICC_PROFILE"));
        using var codec = SKCodec.Create(new MemoryStream(file.Bytes));
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
    }

    [Fact]
    public async Task Jpeg_IsTurnedTheWayItsExifOrientationSays()
    {
        // 40 x 20, left half red and right half blue, tagged "rotate 90 degrees clockwise".
        var jpeg = ImageFixtures.WithSegments(ImageFixtures.HalfAndHalfJpeg(40, 20),
            ImageFixtures.ExifSegment(6, "photo", "nowhere"));
        using (var original = SKCodec.Create(new MemoryStream(jpeg)))
            Assert.Equal(SKEncodedOrigin.RightTop, original.EncodedOrigin);

        var file = await AcceptedAsync(jpeg);

        Assert.Equal((20, 40), (file.Width, file.Height));
        using var bitmap = SKBitmap.Decode(file.Bytes);
        Assert.Equal((20, 40), (bitmap.Width, bitmap.Height));
        AssertNear(ImageFixtures.Red, bitmap.GetPixel(10, 5));
        AssertNear(ImageFixtures.Blue, bitmap.GetPixel(10, 35));
        using var codec = SKCodec.Create(new MemoryStream(file.Bytes));
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
    }

    private static void AssertNear(SKColor expected, SKColor actual)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) < 40
            && Math.Abs(expected.Green - actual.Green) < 40
            && Math.Abs(expected.Blue - actual.Blue) < 40, $"Expected about {expected}, got {actual}.");
    }

    [Fact]
    public async Task StaticWebP_StaysWebP_AndLosslessStaysLossless()
    {
        var lossless = await AcceptedAsync(ImageFixtures.WebPLossless(6, 4));
        var lossy = await AcceptedAsync(ImageFixtures.WebPLossy(8, 8));

        Assert.Equal(MediaFormat.WebP, lossless.Format);
        Assert.Equal((6, 4), (lossless.Width, lossless.Height));
        Assert.True(WebPContainer.IsLossless(lossless.Bytes));
        Assert.Equal(MediaFormat.WebP, lossy.Format);
        Assert.False(WebPContainer.IsLossless(lossy.Bytes));
        Assert.False(lossless.IsAnimated);
    }

    [Fact]
    public async Task AnimatedGif_KeepsItsFrames_WithMetadataStripped()
    {
        var gif = ImageFixtures.Gif(5, 3, [0, 1, 0, 1], comment: "SECRET-COMMENT", xmp: "SECRET-XMP");

        var file = await AcceptedAsync(gif);

        Assert.Equal(MediaFormat.Gif, file.Format);
        Assert.True(file.IsAnimated);
        Assert.Equal((5, 3), (file.Width, file.Height));
        Assert.False(ImageFixtures.Contains(file.Bytes, "SECRET"));
        using var codec = SKCodec.Create(new MemoryStream(file.Bytes));
        Assert.Equal(4, codec.FrameCount);
    }

    [Fact]
    public async Task StaticGif_IsKeptAsAGif_NotMarkedAnimated()
    {
        var file = await AcceptedAsync(ImageFixtures.Gif(2, 2, [1], loop: false, comment: "SECRET"));

        Assert.Equal(MediaFormat.Gif, file.Format);
        Assert.False(file.IsAnimated);
        Assert.False(ImageFixtures.Contains(file.Bytes, "SECRET"));
    }

    [Fact]
    public async Task AnimatedWebP_KeepsItsFrames_WithMetadataStripped()
    {
        var webp = ImageFixtures.AnimatedWebP(4, 4, [ImageFixtures.Red, ImageFixtures.Blue, ImageFixtures.Red],
            exif: "SECRET-EXIF", xmp: "SECRET-XMP");

        var file = await AcceptedAsync(webp);

        Assert.Equal(MediaFormat.WebP, file.Format);
        Assert.True(file.IsAnimated);
        Assert.False(ImageFixtures.Contains(file.Bytes, "SECRET"));
        using var codec = SKCodec.Create(new MemoryStream(file.Bytes));
        Assert.Equal(3, codec.FrameCount);
    }

    [Fact]
    public async Task Animation_WithTooManyFrames_IsRefused()
    {
        var gif = ImageFixtures.Gif(1, 1, Enumerable.Repeat(0, MediaUploadLimits.MaxFrames + 1).ToArray());

        var result = await CleanAsync(gif);

        Assert.Equal(MediaRejections.TooManyFrames, result.Rejection);
    }

    [Fact]
    public async Task HugeCanvas_IsRefusedFromTheHeader_BeforeDecoding()
    {
        // 20000 x 20000 would need 1.6 GB to decode; the file is under 200 bytes.
        var bomb = ImageFixtures.PngClaiming(20_000, 20_000);
        Assert.True(bomb.Length < 200);

        var result = await CleanAsync(bomb);

        Assert.Equal(MediaRejections.TooWide, result.Rejection);
    }

    [Fact]
    public async Task OneSideOverTheLimit_IsRefused()
    {
        var result = await CleanAsync(ImageFixtures.PngClaiming(MediaUploadLimits.MaxSide + 1, 1));

        Assert.Equal(MediaRejections.TooWide, result.Rejection);
    }

    [Fact]
    public async Task TooManyPixels_IsRefusedFromTheHeader()
    {
        // 8000 x 7000 = 56 megapixels: each side is allowed, the area is not.
        var result = await CleanAsync(ImageFixtures.PngClaiming(8_000, 7_000));

        Assert.Equal(MediaRejections.TooManyPixels, result.Rejection);
    }

    [Fact]
    public async Task FileOverTwentyMegabytes_IsRefused_WithoutReadingItAll()
    {
        var stream = new CountingStream(MediaUploadLimits.MaxFileBytes * 3);

        var result = await _sut.CleanAsync(stream);

        Assert.Equal(MediaRejections.TooLarge, result.Rejection);
        Assert.True(stream.BytesRead <= MediaUploadLimits.MaxFileBytes + 1024 * 1024);
    }

    [Fact]
    public async Task EmptyFile_IsRefused()
    {
        Assert.Equal(MediaRejections.Empty, (await CleanAsync([])).Rejection);
    }

    [Fact]
    public async Task GifHeaderFollowedByJavaScript_IsRefused()
    {
        // A GIF/JavaScript polyglot: a valid GIF signature that is also a script.
        var polyglot = Encoding.ASCII.GetBytes("GIF89a=1;alert(document.cookie);//");

        var result = await CleanAsync(polyglot);

        Assert.Equal(MediaRejections.Unreadable, result.Rejection);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body><script>alert(1)</script></body></html>")]
    [InlineData("<html><body><script>alert(1)</script></body></html>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task HtmlNamedAsAnImage_IsRefused(string html)
    {
        var result = await CleanAsync(Encoding.UTF8.GetBytes(html));

        Assert.False(result.IsAccepted);
    }

    [Theory]
    [InlineData("%PDF-1.7 not an image")]
    [InlineData("MZ executable")]
    public async Task OtherFiles_AreRefusedAsNotAnImage(string text)
    {
        Assert.Equal(MediaRejections.NotAnImage, (await CleanAsync(Encoding.ASCII.GetBytes(text))).Rejection);
    }

    [Fact]
    public async Task PngThatIsCutShort_IsRefused()
    {
        var png = ImageFixtures.Png(32, 32);

        var result = await CleanAsync(png[..(png.Length / 2)]);

        Assert.Equal(MediaRejections.Unreadable, result.Rejection);
    }

    [Fact]
    public async Task Svg_IsSanitised()
    {
        var file = await AcceptedAsync(Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"20\" onload=\"alert(1)\"><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Equal(MediaFormat.Svg, file.Format);
        Assert.Equal("image/svg+xml", file.ContentType);
        Assert.Equal((10, 20), (file.Width, file.Height));
        Assert.DoesNotContain("alert", ImageFixtures.Text(file.Bytes));
    }

    // A stream of zeros that counts what was read, so a test can see the reader stopped early.
    private sealed class CountingStream(long length) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - BytesRead);
            Array.Clear(buffer, offset, n);
            BytesRead += n;
            return n;
        }
    }
}
