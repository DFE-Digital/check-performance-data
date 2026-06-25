namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A single content block in an export bundle, keyed by its stable Key. Only the current value
// is exported — version history is not part of the v1 bundle.
public sealed record ContentBlockBundleItem
{
    public string Key { get; init; } = string.Empty;
    public string BlockType { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
