using DfE.CheckPerformanceData.Application.Media.Upload;
using SkiaSharp;

namespace DfE.CheckPerformanceData.Infrastructure.Media;

/// <summary>
/// Checks and cleans PNG, JPEG, GIF and WebP uploads with Skia.
///
/// The codec reads only the header first, so the size, pixel and frame limits are checked before
/// any pixels are decoded. A static PNG, JPEG or WebP is decoded into sRGB, turned the way its
/// EXIF orientation says, and encoded again in the same format (JPEG at quality 85; a lossless
/// WebP stays lossless). The new file is written without a colour profile, so nothing from the
/// upload's EXIF, GPS, XMP or ICC data survives. Skia cannot write GIF or animated WebP, so those
/// keep their original frames: every frame is decoded to prove the file is sound, the metadata
/// blocks are cut out, and the result must decode to the same frames.
/// </summary>
public sealed class SkiaRasterImageCleaner : IRasterImageCleaner
{
    private const int JpegQuality = 85;
    private const int WebPLossyQuality = 85;
    private const int WebPLosslessEffort = 75;

    public MediaCheckResult Clean(byte[] bytes, MediaFormat format)
    {
        if (format == MediaFormat.Svg) throw new ArgumentException("SVG is not a raster format.", nameof(format));
        try
        {
            // A GIF's block structure is checked (and its metadata cut) before Skia sees it, which
            // also refuses a GIF header with a script after it.
            var input = format == MediaFormat.Gif ? GifMetadataStripper.Strip(bytes) : bytes;

            using var data = SKData.CreateCopy(input);
            using var codec = SKCodec.Create(data);
            if (codec is null || FormatOf(codec.EncodedFormat) != format) return Reject(MediaRejections.Unreadable);

            var (width, height) = (codec.Info.Width, codec.Info.Height);
            var sizeProblem = CheckSize(width, height);
            if (sizeProblem is not null) return Reject(sizeProblem);
            var frames = Math.Max(1, codec.FrameCount);
            if (frames > MediaUploadLimits.MaxFrames) return Reject(MediaRejections.TooManyFrames);

            if (format == MediaFormat.Gif)
                return KeepOriginalFrames(codec, input, format, frames);
            if (format == MediaFormat.WebP && frames > 1)
                return KeepOriginalFrames(codec, WebPContainer.StripMetadata(input), format, frames);
            return ReEncode(codec, bytes, format);
        }
        catch (InvalidDataException)
        {
            return Reject(MediaRejections.Unreadable);
        }
    }

    private static MediaCheckResult Reject(string reason) => MediaCheckResult.Reject(reason);

    private static MediaFormat? FormatOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => MediaFormat.Png,
        SKEncodedImageFormat.Jpeg => MediaFormat.Jpeg,
        SKEncodedImageFormat.Gif => MediaFormat.Gif,
        SKEncodedImageFormat.Webp => MediaFormat.WebP,
        _ => null
    };

    private static string? CheckSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return MediaRejections.Unreadable;
        if (width > MediaUploadLimits.MaxSide || height > MediaUploadLimits.MaxSide) return MediaRejections.TooWide;
        if ((long)width * height > MediaUploadLimits.MaxPixels) return MediaRejections.TooManyPixels;
        return null;
    }

    private static MediaCheckResult KeepOriginalFrames(SKCodec codec, byte[] stripped, MediaFormat format, int frames)
    {
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if ((long)width * height * frames > MediaUploadLimits.MaxAnimationPixels)
            return Reject(MediaRejections.AnimationTooLarge);
        if (!DecodesEveryFrame(codec, frames)) return Reject(MediaRejections.Unreadable);

        // The stripped file must still be the same picture.
        using var check = SKCodec.Create(new MemoryStream(stripped));
        if (check is null || check.Info.Width != width || check.Info.Height != height || Math.Max(1, check.FrameCount) != frames)
            return Reject(MediaRejections.Unreadable);

        return MediaCheckResult.Accept(new CleanedMediaFile(stripped, format, width, height, IsAnimated: frames > 1));
    }

    private static bool DecodesEveryFrame(SKCodec codec, int frames)
    {
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKBitmap(info);
        for (var frame = 0; frame < frames; frame++)
        {
            // Decoding on top of the previous frame is cheapest; the codec refuses that when this
            // frame depends on an earlier one, and then decodes what it needs itself.
            var result = frame == 0
                ? SKCodecResult.InvalidParameters
                : codec.GetPixels(info, canvas.GetPixels(), new SKCodecOptions(frame, frame - 1));
            if (result == SKCodecResult.InvalidParameters)
                result = codec.GetPixels(info, canvas.GetPixels(), new SKCodecOptions(frame));
            if (result != SKCodecResult.Success) return false;
        }
        return true;
    }

    private static MediaCheckResult ReEncode(SKCodec codec, byte[] original, MediaFormat format)
    {
        var alpha = format == MediaFormat.Jpeg || codec.Info.AlphaType == SKAlphaType.Opaque
            ? SKAlphaType.Opaque
            : SKAlphaType.Premul;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, alpha, SKColorSpace.CreateSrgb());

        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success) return Reject(MediaRejections.Unreadable);

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var picture = oriented ?? decoded;

        // The pixels are already sRGB. Encoding them untagged writes no colour profile at all.
        var untagged = new SKPixmap(picture.Info.WithColorSpace(null), picture.GetPixels(), picture.RowBytes);
        using var encoded = format switch
        {
            MediaFormat.Png => untagged.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6)),
            MediaFormat.Jpeg => untagged.Encode(new SKJpegEncoderOptions(
                JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)),
            _ => WebPContainer.IsLossless(original)
                ? untagged.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, WebPLosslessEffort))
                : untagged.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebPLossyQuality))
        };
        if (encoded is null) return Reject(MediaRejections.Unreadable);

        return MediaCheckResult.Accept(new CleanedMediaFile(encoded.ToArray(), format, picture.Width, picture.Height, IsAnimated: false));
    }

    // A new bitmap turned upright, or null when the picture is already upright. The matrices map
    // a stored pixel to where it is displayed, as in the EXIF orientation definitions.
    private static SKBitmap? Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width, h = source.Height;
        SKMatrix? matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null
        };
        if (matrix is null) return null;

        var swapsSides = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var turned = new SKBitmap(swapsSides
            ? source.Info.WithSize(source.Height, source.Width)
            : source.Info);
        using var canvas = new SKCanvas(turned);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.SetMatrix(matrix.Value);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        canvas.Flush();
        return turned;
    }
}
