namespace DfE.CheckPerformanceData.Application.HomeBanners;

/// <summary>Everything an editor can change on a banner: what a save writes and a version snapshots.</summary>
public sealed record HomeBannerContent(
    string Heading,
    string Body,
    bool IsEnabled,
    DateTime? ShowFrom,
    DateTime? ShowUntil);
