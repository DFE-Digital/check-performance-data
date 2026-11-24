namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Makes a resized or cropped WebP from a stored static image (PNG, JPEG or static WebP). The
/// caller gives the layout from <see cref="MediaRenditionGeometry"/> and holds a
/// <see cref="MediaProcessingGate"/> place while this runs, because it decodes the whole image.
/// </summary>
public interface IMediaRenditionRenderer
{
    byte[] Render(byte[] source, MediaRenditionLayout layout);
}
