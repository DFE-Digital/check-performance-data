namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>The content type and file extension each accepted format is stored and served with.</summary>
public static class MediaFormats
{
    public static string ContentType(MediaFormat format) => format switch
    {
        MediaFormat.Png => "image/png",
        MediaFormat.Jpeg => "image/jpeg",
        MediaFormat.Gif => "image/gif",
        MediaFormat.WebP => "image/webp",
        MediaFormat.Svg => "image/svg+xml",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    public static string Extension(MediaFormat format) => format switch
    {
        MediaFormat.Png => "png",
        MediaFormat.Jpeg => "jpg",
        MediaFormat.Gif => "gif",
        MediaFormat.WebP => "webp",
        MediaFormat.Svg => "svg",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };
}
