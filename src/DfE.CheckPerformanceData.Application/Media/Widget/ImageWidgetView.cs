namespace DfE.CheckPerformanceData.Application.Media.Widget;

/// <summary>
/// What the Image widget's view writes. <see cref="SrcSet"/> and <see cref="Sizes"/> are null for
/// an image that is never resized (SVG, animation). <see cref="Width"/> and <see cref="Height"/> are
/// the shape of the image shown, so the page does not move when it loads; null when not known.
/// <see cref="Alt"/> is empty for a decorative image.
/// </summary>
public sealed record ImageWidgetView(
    string Src,
    string? SrcSet,
    string? Sizes,
    int? Width,
    int? Height,
    string Alt,
    string CssClass,
    string? Caption,
    string? Credit,
    string? Href);
