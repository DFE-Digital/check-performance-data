namespace DfE.CheckPerformanceData.Persistence.Entities;

public sealed class ContentBlock
{
    public int Id { get; set; }
    // Stable cross-environment identity, preserved through content-staging export/import so the
    // same block is recognised across environments even if its Key changes.
    public Guid ContentId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    // The request path the block was most recently rendered on, recorded automatically by the
    // editable view components. Surfaces "which page this block sits on" for the management page,
    // including blocks placed under dynamically-generated keys that code cannot be scanned for.
    public string? LastSeenPath { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public ICollection<ContentBlockVersion> Versions { get; set; } = [];
}
