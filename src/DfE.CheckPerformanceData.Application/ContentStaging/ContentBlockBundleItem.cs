namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A single content block in an export bundle. Identity is carried by the stable GUID Id (not the
// Key, which an editor may change); the Key remains the value the app fetches the block by. Only
// the current value is exported — version history is not part of the v1 bundle.
public sealed record ContentBlockBundleItem
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string BlockType { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
