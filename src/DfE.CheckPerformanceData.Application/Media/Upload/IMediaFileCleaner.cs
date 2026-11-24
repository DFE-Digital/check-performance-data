namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// The single entry point every upload goes through: reads the file (refusing anything over the
/// size limit), decides its type from its content, then cleans it as a raster image or an SVG.
/// </summary>
public interface IMediaFileCleaner
{
    Task<MediaCheckResult> CleanAsync(Stream content, CancellationToken ct = default);
}
