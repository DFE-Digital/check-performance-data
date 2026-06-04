namespace DfE.CheckPerformanceData.Application.RulesEngine;

public sealed class RulesEngineOptions
{
    public const string SectionName = "RulesEngineOptions";

    public int RetryDelayMs { get; set; } = 5000;
    public string QueueName { get; set; } = "performance-requests";
    public int MaxMessagesPerPoll { get; set; } = 1;
    public int EmptyQueueDelayMs { get; set; } = 2000;
    public long MaxDequeueCount { get; set; } = 5;
    public TimeSpan? VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);
}