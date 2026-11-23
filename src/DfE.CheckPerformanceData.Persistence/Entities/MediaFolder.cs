namespace DfE.CheckPerformanceData.Persistence.Entities;

// A folder of the media library. A null ParentId puts it at the top of the library. The tree is
// for editors only: an image's URL and id do not change when it moves to another folder.
public sealed class MediaFolder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // lower(name), made by the database. The unique index on (ParentId, NameKey) stops two folders
    // in one place from sharing a name that differs only in case.
    public string NameKey { get; private set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
