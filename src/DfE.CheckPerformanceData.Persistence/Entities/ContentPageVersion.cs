namespace DfE.CheckPerformanceData.Persistence.Entities;

// An immutable snapshot of a content page taken at publish time. Snapshot is the serialised content
// tree (jsonb); VersionNumber is the integer assigned in sequence. Restoring an old version writes a
// new version pointing at the same content, so the history only ever grows.
public sealed class ContentPageVersion
{
    public int Id { get; set; }
    public int ContentPageId { get; set; }
    public ContentPage ContentPage { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string Snapshot { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
