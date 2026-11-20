namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>The reasons an upload is refused, in plain English for the editor who chose the file.</summary>
public static class MediaRejections
{
    public const string Empty = "The selected file is empty.";
    public const string TooLarge = "The selected file must be smaller than 20 MB.";
    public const string NotAnImage = "The selected file must be a PNG, JPEG, GIF, WebP or SVG image.";
    public const string Unreadable = "The selected file could not be read as an image. It may be damaged.";
    public const string TooManyPixels = "The image must be 50 megapixels or smaller.";
    public const string TooWide = "The image must be 10,000 pixels or smaller in width and height.";
    public const string TooManyFrames = "An animated image must have 500 frames or fewer.";
    public const string AnimationTooLarge = "The animation is too large. Use fewer frames or smaller dimensions.";
    public const string SvgUnreadable =
        "The selected SVG could not be read. It may be damaged, too large or use features that are not allowed.";
    public const string ReplacedConcurrently =
        "Someone else replaced this image at the same time. Reload the page and try again.";
}
