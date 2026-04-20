namespace DfE.CheckPerformanceData.Persistence.Entities;

public sealed class ContentBlockVersion
{
    public int Id { get; set; }
    public int ContentBlockId { get; set; }
    public ContentBlock ContentBlock { get; set; } = null!;
    public string Value { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
