namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A single content block in an export bundle. Identity is carried by the stable GUID Id (not the
// Key, which an editor may change); the Key remains the value the app fetches the block by. Only
// the current value is exported — version history is not part of the v1 bundle.
public sealed record ContentBlockBundleItem
{
    public Guid Id { get; init; }
    private readonly string _key = string.Empty;
    public string Key { get => _key; init => _key = BundleMemberDefaults.OrEmpty(value); }
    private readonly string _blockType = string.Empty;
    public string BlockType { get => _blockType; init => _blockType = BundleMemberDefaults.OrEmpty(value); }
    private readonly string _value = string.Empty;
    public string Value { get => _value; init => _value = BundleMemberDefaults.OrEmpty(value); }

    /// <summary>
    /// Whether the block appears in help-search. Defaults to true so bundles produced by earlier
    /// exporters (without this field) round-trip as searchable, matching the DB default.
    /// </summary>
    public bool AppearInSearch { get; init; } = true;

    /// <summary>
    /// Free-text search keywords. Nullable — older bundles omit this field and it round-trips
    /// as null (matching the DB default). Weighted highest in the search index at import time.
    /// </summary>
    public string? Keywords { get; init; }
}
