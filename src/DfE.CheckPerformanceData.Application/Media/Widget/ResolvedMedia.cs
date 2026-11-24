namespace DfE.CheckPerformanceData.Application.Media.Widget;

/// <summary>A library image and the version of it that a page shows.</summary>
public sealed record ResolvedMedia(MediaAssetDto Asset, MediaAssetVersionDto Version);
