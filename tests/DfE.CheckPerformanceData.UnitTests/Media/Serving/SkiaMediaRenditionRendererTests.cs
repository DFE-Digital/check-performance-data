using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Infrastructure.Media;
using SkiaSharp;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;

// The renderer makes a WebP of exactly the chosen area and size, from the stored PNG, JPEG or WebP.
public sealed class SkiaMediaRenditionRendererTests
{
    private readonly SkiaMediaRenditionRenderer _sut = new();

    // Left half red, right half blue.
    private static byte[] TwoColourImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        }
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static SKBitmap Decode(byte[] bytes, out SKEncodedImageFormat format)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        format = codec.EncodedFormat;
        return SKBitmap.Decode(bytes);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void ScalesTheWholeImage_ToAWebPOfTheChosenSize(SKEncodedImageFormat input)
    {
        var layout = new MediaRenditionLayout(new MediaPixelRect(0, 0, 400, 200), 100, 50);

        using var output = Decode(_sut.Render(TwoColourImage(400, 200, input), layout), out var format);

        Assert.Equal(SKEncodedImageFormat.Webp, format);
        Assert.Equal((100, 50), (output.Width, output.Height));
    }

    [Fact]
    public void TakesOnlyTheChosenArea()
    {
        // The right half is all blue.
        var layout = new MediaRenditionLayout(new MediaPixelRect(200, 0, 200, 200), 100, 100);

        using var output = Decode(_sut.Render(TwoColourImage(400, 200, SKEncodedImageFormat.Png), layout), out _);

        var pixel = output.GetPixel(5, 50);
        Assert.True(pixel.Blue > 200 && pixel.Red < 60, $"Expected blue, got {pixel}");
        pixel = output.GetPixel(95, 50);
        Assert.True(pixel.Blue > 200 && pixel.Red < 60, $"Expected blue, got {pixel}");
    }

    [Fact]
    public void KeepsTransparency()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);

        using var output = Decode(_sut.Render(data.ToArray(), new MediaRenditionLayout(new MediaPixelRect(0, 0, 40, 40), 20, 20)), out _);

        Assert.Equal(0, output.GetPixel(10, 10).Alpha);
    }

    [Fact]
    public void RefusesAFileItCannotRead()
    {
        Assert.Throws<InvalidDataException>(() => _sut.Render([1, 2, 3], new MediaRenditionLayout(new MediaPixelRect(0, 0, 1, 1), 1, 1)));
    }

    [Fact]
    public void RefusesALayoutOutsideTheImage()
    {
        var bytes = TwoColourImage(40, 20, SKEncodedImageFormat.Png);

        Assert.Throws<InvalidDataException>(() => _sut.Render(bytes, new MediaRenditionLayout(new MediaPixelRect(30, 0, 20, 20), 10, 10)));
    }
}
