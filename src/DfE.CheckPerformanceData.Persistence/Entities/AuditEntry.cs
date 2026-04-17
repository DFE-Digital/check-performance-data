namespace DfE.CheckPerformance.Persistence.Entities;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? ChangedColumns { get; set; }
    public DateTime Timestamp { get; set; }
    public string? UserId { get; set; }
}
