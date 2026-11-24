namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// One use of a library image in stored content, with the text a reader gets for it.
/// <see cref="Alt"/> is the text the content gives: null when it gives none, so the library's
/// default alt text shows. <see cref="Decorative"/> is true for an image marked decorative, which
/// needs no alt text. <see cref="Caption"/> is the caption shown with the image, if any.
/// <see cref="Location"/> is a <see cref="MediaUsageLocations"/> value; <see cref="WidgetPath"/>
/// is the tree path of the widget, or null when the content is not a widget tree.
/// </summary>
public sealed record MediaImageUse(
    Guid AssetId,
    string Location,
    string? WidgetPath,
    string? Alt,
    bool Decorative,
    string? Caption);
