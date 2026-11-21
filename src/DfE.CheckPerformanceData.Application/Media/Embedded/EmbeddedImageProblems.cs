namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Why an image embedded in page content cannot move to the media library, in plain English for
/// the editor. The upload pipeline can refuse an image for its own reasons too (see MediaRejections).
/// </summary>
public static class EmbeddedImageProblems
{
    public const string NotImageData =
        "This is not image data, so the page does not show it. Remove it from the page.";
    public const string Unreadable = "The image data could not be read. It may be damaged.";
    public const string Empty = "The image data is empty.";
    public const string NotSupported = "The image is not a PNG, JPEG, GIF, WebP or SVG image.";
    public const string TooLarge = "The image is larger than 20 MB.";
    public const string ChangedWhileMoving =
        "The page changed while its images were moving, so it was not changed. Run the move again.";
}
