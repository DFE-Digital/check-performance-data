namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// An upload after checking and cleaning: the bytes to store (re-encoded, metadata stripped or
/// sanitised), its format, its displayed size (after EXIF orientation) and whether it animates.
/// Width and Height are null for an SVG that declares no size.
/// </summary>
public sealed record CleanedMediaFile(byte[] Bytes, MediaFormat Format, int? Width, int? Height, bool IsAnimated)
{
    public string ContentType => MediaFormats.ContentType(Format);
}
