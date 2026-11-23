namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>One way that current content shows an image, with the pages and blocks that show it that way.</summary>
public sealed record MediaVariant(MediaVariantShape Shape, IReadOnlyList<MediaVariantUse> Uses);
