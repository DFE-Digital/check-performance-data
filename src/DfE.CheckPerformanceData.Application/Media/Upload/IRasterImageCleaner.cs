namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// Checks and cleans a PNG, JPEG, GIF or WebP whose type was already sniffed from its bytes.
/// A static image gets its EXIF orientation applied and is re-encoded, which drops EXIF, GPS,
/// XMP and colour profiles. A GIF, or an animated WebP, is validated by decoding every frame and
/// is stored as its original bytes with the metadata blocks removed.
/// </summary>
public interface IRasterImageCleaner
{
    MediaCheckResult Clean(byte[] bytes, MediaFormat format);
}
