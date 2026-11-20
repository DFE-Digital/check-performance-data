namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// What the upload pipeline accepts. The pixel and side limits are checked from the image header
/// before any pixels are decoded, so a small file that claims a huge canvas (a decompression bomb)
/// is refused without allocating for it. Animations also have a frame limit and a budget for the
/// pixels of every frame together, because each frame is decoded to validate the file.
/// </summary>
public static class MediaUploadLimits
{
    public const long MaxFileBytes = 20L * 1024 * 1024;
    public const long MaxPixels = 50_000_000;
    public const int MaxSide = 10_000;
    public const int MaxFrames = 500;
    public const long MaxAnimationPixels = 250_000_000;

    /// <summary>The most characters an SVG may have, and how deeply its elements may nest.</summary>
    public const int MaxSvgCharacters = 5_000_000;
    public const int MaxSvgDepth = 256;
}
