namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// A resized or cropped copy of a stored image, as a media URL asks for it. Every part is
/// optional; at least one is set. Values are already checked and rounded (see
/// <see cref="MediaRenditionQuery"/>), so two equal renditions give the same image.
/// </summary>
/// <param name="Width">One of <see cref="MediaRenditionQuery.AllowedWidths"/>; never larger than the area shown.</param>
/// <param name="Crop">The part of the image to keep, as fractions of its width and height.</param>
/// <param name="Aspect">A shape to cut from the cropped area, for example 16:9.</param>
/// <param name="Focal">The point to keep in view when cutting to <paramref name="Aspect"/>, as fractions of the whole image.</param>
public sealed record MediaRendition(int? Width, MediaCrop? Crop, MediaAspectRatio? Aspect, MediaFocalPoint? Focal);
