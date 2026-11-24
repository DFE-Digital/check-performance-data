namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>A content block with its current value, and the images that its <c>media_usages</c> rows record.</summary>
public sealed record MediaVariantBlock(int Id, string Key, string Value, IReadOnlyList<Guid> RecordedAssetIds);
