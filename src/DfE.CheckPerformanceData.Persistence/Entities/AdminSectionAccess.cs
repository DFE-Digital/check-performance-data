namespace DfE.CheckPerformanceData.Persistence.Entities;

// A permission row: the presence of a (RoleName, SectionKey) pair grants that role access to that
// section of admin. Absence denies. There is no separate "deny" flag — the row set is the entire
// truth. The unique index on (RoleName, SectionKey) keeps the set from double-counting.
public sealed class AdminSectionAccess
{
    public int Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string SectionKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
