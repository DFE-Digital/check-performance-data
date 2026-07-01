namespace DfE.CheckPerformanceData.Persistence.Entities;

// The single hierarchy for all CMS pages. Self-referencing (ParentId), GUID-keyed. Path is the
// materialised full slug path, kept unique among live nodes for O(1) route resolution.
public sealed class PageNode
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Segment { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PageType { get; set; } = "folder"; // content | wiki | folder
    public DateTime CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedBy { get; set; }
    public ICollection<PageNodeVersion> Versions { get; set; } = [];
}
