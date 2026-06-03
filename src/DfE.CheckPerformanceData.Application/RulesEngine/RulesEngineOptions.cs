namespace DfE.CheckPerformanceData.Application.RulesEngine;

public sealed class RulesEngineOptions
{
    public const string SectionName = "RulesEngineOptions";

    public int RetryDelayMs { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public int MaxMessagesPerPoll { get; set; }
    public int EmptyQueueDelayMs { get; set; }
    public long MaxDequeueCount { get; set; }
    public TimeSpan? VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);
}