namespace DfE.CheckPerformanceData.Domain.Entities;

public sealed class ContentBlock
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public ICollection<ContentBlockVersion> Versions { get; set; } = [];
}
