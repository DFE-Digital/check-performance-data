using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Middleware;

/// <summary>
/// The public media file URLs (/media/...). Middleware that adds a session, buffers HTML bodies or
/// records page views skips them: they are images, cached for a year, and some are large.
/// </summary>
public static class MediaFilePaths
{
    public static bool IsMediaFile(PathString path) =>
        path.StartsWithSegments("/media", StringComparison.OrdinalIgnoreCase);
}
