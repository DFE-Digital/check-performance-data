using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// How one use of a library image in stored content asks the page to show it. An Image widget
/// gives a crop, an aspect ratio and a focal point; each is null when the page would not use it.
/// A rich-text img gives the width and height that the editor set (each null when not set) and its
/// box marker (see <see cref="RichText.RichTextImageBox"/>).
/// <see cref="Location"/> is a <see cref="MediaUsageLocations"/> value; <see cref="WidgetPath"/>
/// is the tree path of the widget, or null when the content is not a widget tree.
/// </summary>
public sealed record MediaImageShapeUse(
    Guid AssetId,
    string Location,
    string? WidgetPath,
    MediaCrop? Crop,
    MediaAspectRatio? Aspect,
    MediaFocalPoint? Focal,
    int? BoxWidth,
    int? BoxHeight,
    string? BoxMarker = null);
