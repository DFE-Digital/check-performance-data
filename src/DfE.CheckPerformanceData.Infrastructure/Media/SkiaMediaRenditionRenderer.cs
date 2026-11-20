using DfE.CheckPerformanceData.Application.Media.Serving;
using SkiaSharp;

namespace DfE.CheckPerformanceData.Infrastructure.Media;

/// <summary>
/// Makes renditions with Skia. The stored file was cleaned on upload, so it is already upright,
/// sRGB and within the size limits. It is decoded, the chosen area is drawn at the chosen size
/// with cubic resampling, and the result is encoded as lossy WebP. Transparency is kept.
/// </summary>
public sealed class SkiaMediaRenditionRenderer : IMediaRenditionRenderer
{
    private const int WebPQuality = 80;

    // Mitchell cubic: a sharp downscale without the ringing of Catmull-Rom.
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public byte[] Render(byte[] source, MediaRenditionLayout layout)
    {
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The stored image cannot be read.");
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        var area = layout.Source;
        if (area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1
            || area.X + area.Width > width || area.Y + area.Height > height
            || layout.Width < 1 || layout.Height < 1)
            throw new InvalidDataException("The rendition area is outside the stored image.");

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The stored image cannot be decoded.");
        using var image = SKImage.FromBitmap(decoded);

        using var output = new SKBitmap(info.WithSize(layout.Width, layout.Height));
        output.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(output))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
        {
            canvas.DrawImage(image, SKRect.Create(area.X, area.Y, area.Width, area.Height),
                SKRect.Create(0, 0, layout.Width, layout.Height), Sampling, paint);
        }

        // Encoded untagged, as on upload: the pixels are sRGB and no colour profile is written.
        var untagged = new SKPixmap(output.Info.WithColorSpace(null), output.GetPixels(), output.RowBytes);
        using var encoded = untagged.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebPQuality))
            ?? throw new InvalidDataException("The rendition could not be encoded.");
        return encoded.ToArray();
    }
}
