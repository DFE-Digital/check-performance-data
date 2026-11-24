namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>The editor-maintained description of a library image. Every field is optional.</summary>
public sealed record MediaAssetMetadata(
    string? DefaultAltText,
    string? Caption,
    string? Credit,
    IReadOnlyList<string> Tags)
{
    public static MediaAssetMetadata Empty { get; } = new(null, null, null, []);
}
