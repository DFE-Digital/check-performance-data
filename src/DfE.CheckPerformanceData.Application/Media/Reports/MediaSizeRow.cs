namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>One row of the size report: an image and the resized copies stored for it.</summary>
public sealed record MediaSizeRow(MediaSizeAsset Asset, MediaRenditionTally Renditions);
