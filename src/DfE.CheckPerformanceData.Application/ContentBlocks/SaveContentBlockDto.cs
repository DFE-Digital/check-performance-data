namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class SaveContentBlockDto
{
    public string Key { get; init; } = string.Empty;
    public string BlockType { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
