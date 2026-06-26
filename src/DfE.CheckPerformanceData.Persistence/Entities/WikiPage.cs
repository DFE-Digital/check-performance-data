using DfE.CheckPerformance.Persistence.Entities;
using NpgsqlTypes;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public sealed class WikiPage
{
    public int Id { get; set; }
    // Stable cross-environment identity. The integer Id is environment-specific; ContentId is
    // preserved through content-staging export/import so the same page is recognised across
    // environments even after its title/slug changes.
    public Guid ContentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string BodyPlainText { get; set; } = string.Empty;   // service writes; populated on Create/Update/Revert/Restore
    public NpgsqlTsVector SearchVector { get; set; } = null!;    // GENERATED STORED — Postgres writes; null-forgiving to match nav-prop pattern
    public int? ParentId { get; set; }
    public WikiPage? Parent { get; set; }
    public ICollection<WikiPage> Children { get; set; } = [];
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public ICollection<WikiPageVersion> Versions { get; set; } = [];
}
