namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// A library image as editors see it: its descriptive metadata plus the version pages show now.
/// Alt text, caption and credit here are defaults; a page can override them where it uses the image.
/// </summary>
public sealed record MediaAssetDto(
    Guid Id,
    string FileName,
    string Slug,
    string? DefaultAltText,
    string? Caption,
    string? Credit,
    IReadOnlyList<string> Tags,
    int CurrentVersion,
    MediaAssetVersionDto Latest,
    DateTime CreatedAtUtc,
    string? CreatedBy,
    DateTime UpdatedAtUtc,
    string? UpdatedBy);
