namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class ContentBlockDto
{
    public int Id { get; init; }
    public Guid ContentId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string BlockType { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? ValueHtml { get; init; }
    public string? LastSeenPath { get; init; }
    public DateTime? LastSeenAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
