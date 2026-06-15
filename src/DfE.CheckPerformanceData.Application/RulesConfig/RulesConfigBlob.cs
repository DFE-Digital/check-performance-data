namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>Raw content of a config blob plus its current ETag (for optimistic concurrency).</summary>
public sealed record RulesConfigBlob(string Content, string? ETag);
