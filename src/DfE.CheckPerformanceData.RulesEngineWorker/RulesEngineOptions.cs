namespace DfE.CheckPerformanceData.RulesEngineWorker;

public sealed class RulesEngineOptions
{
    public required int RetryDelayMs { get; set; }
    public required string QueueName { get; set; }
    public required int MaxMessagesPerPoll { get; set; }
    public required int EmptyQueueDelayMs { get; set; }
    public required long MaxDequeueCount { get; set; }
}