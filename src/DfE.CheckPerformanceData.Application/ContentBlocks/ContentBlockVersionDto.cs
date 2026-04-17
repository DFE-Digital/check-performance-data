namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class ContentBlockVersionDto
{
    public int Id { get; init; }
    public int VersionNumber { get; init; }
    public string Value { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
