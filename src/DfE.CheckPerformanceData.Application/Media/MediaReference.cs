namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// One use of a library image in a page version's content. <see cref="Location"/> is a
/// <see cref="MediaUsageLocations"/> value. <see cref="WidgetPath"/> is the tree path of the widget
/// that holds the image (an Image widget, or the rich-text widget around an image), or null when
/// the content is not a widget tree.
/// </summary>
public sealed record MediaReference(Guid AssetId, string Location, string? WidgetPath);
