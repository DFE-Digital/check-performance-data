namespace DfE.CheckPerformanceData.Persistence.Entities;

// A composable content page: an always-editable draft content tree (jsonb) plus a linear history of
// published versions. PublishedVersionNumber names the version the public route renders; it is null
// until the page is first published. ContentId is the stable cross-environment identity (the integer
// Id is environment-specific), mirroring WikiPage/ContentBlock.
public sealed class ContentPage
{
    public int Id { get; set; }
    public Guid ContentId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public string Layout { get; set; } = "nav-content";
    public string DraftContent { get; set; } = "[]";
    public int? PublishedVersionNumber { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public ICollection<ContentPageVersion> Versions { get; set; } = [];
}
