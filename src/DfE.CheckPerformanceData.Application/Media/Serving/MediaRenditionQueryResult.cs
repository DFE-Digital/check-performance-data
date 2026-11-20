namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// A media URL's query string after reading. When <see cref="IsValid"/> is true and
/// <see cref="Rendition"/> is null, the URL asks for the stored file. <see cref="Signature"/> is
/// the sig value as sent; it is not checked here.
/// </summary>
public sealed record MediaRenditionQueryResult(bool IsValid, MediaRendition? Rendition, string? Signature)
{
    public static MediaRenditionQueryResult Invalid { get; } = new(false, null, null);
}
