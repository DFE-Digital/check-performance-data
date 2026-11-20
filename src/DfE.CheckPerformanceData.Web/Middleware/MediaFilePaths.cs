using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Middleware;

/// <summary>
/// The public media file URLs: /media/{id}/{version}/{file}, the route MediaFileController owns.
/// Middleware that adds a session, buffers HTML bodies or records page views skips them: they are
/// images, cached for a year, and some are large. Any other /media path is not a media file and
/// keeps the session, because it falls through to the content page route, which needs one.
/// </summary>
public static class MediaFilePaths
{
    public static bool IsMediaFile(PathString path) =>
        path.StartsWithSegments("/media", StringComparison.OrdinalIgnoreCase, out var rest)
        && rest.Value is { } tail
        && tail.Split('/') is ["", { Length: > 0 }, { Length: > 0 }, { Length: > 0 }];
}
