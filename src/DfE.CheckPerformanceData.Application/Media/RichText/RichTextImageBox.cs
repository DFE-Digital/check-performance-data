using AngleSharp.Dom;

namespace DfE.CheckPerformanceData.Application.Media.RichText;

/// <summary>
/// The box marker of a rich-text library image: <c>data-media-box="fixed"</c> or
/// <c>data-media-box="follow"</c>. The editor's image dialog always saves a width and a height, so
/// the two numbers alone cannot tell a box that the editor shaped on purpose from one that only
/// copies the shape of the file. The marker records which one it is, when the text is saved.
///
/// <list type="bullet">
/// <item><b>fixed</b>: the box is another shape than the file it was set on. The page keeps the
/// width and the height, also after the image is replaced.</item>
/// <item><b>follow</b>: the box has the shape of the file. The page keeps the width, and the height
/// follows the shape of the version shown, so a replacement of another shape is not stretched.</item>
/// <item>No marker (or an unknown value): the page keeps the width and the height.</item>
/// </list>
///
/// The editor script writes the marker on each save, from the file that the editor shows. Moving
/// an embedded image into the library writes it from the new library image.
/// </summary>
public static class RichTextImageBox
{
    public const string Attribute = "data-media-box";
    public const string Fixed = "fixed";
    public const string Follow = "follow";

    // The dialog works out one side from the other and rounds it, so a box within this many
    // pixels of the file's shape has that shape. The editor script uses the same number.
    private const double TolerancePixels = 1;

    /// <summary><see cref="Fixed"/>, <see cref="Follow"/>, or null for no marker or an unknown value.</summary>
    public static string? Read(IElement img) =>
        img.GetAttribute(Attribute)?.Trim().ToLowerInvariant() switch
        {
            Fixed => Fixed,
            Follow => Follow,
            _ => null,
        };

    /// <summary>
    /// True when the box has the shape of a file of that size: the height is within a pixel of the
    /// height that the width gives, or the width is within a pixel of the width that the height gives.
    /// </summary>
    public static bool HasShapeOf(int width, int height, int fileWidth, int fileHeight) =>
        Math.Abs(height - width * (double)fileHeight / fileWidth) <= TolerancePixels + 0.5
        || Math.Abs(width - height * (double)fileWidth / fileHeight) <= TolerancePixels + 0.5;

    /// <summary>The marker for a box that the editor set on a file of that size.</summary>
    public static string For(int width, int height, int fileWidth, int fileHeight) =>
        HasShapeOf(width, height, fileWidth, fileHeight) ? Follow : Fixed;
}
