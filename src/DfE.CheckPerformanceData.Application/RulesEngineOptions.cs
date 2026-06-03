namespace DfE.CheckPerformanceData.Application;

public class RulesEngineOptions
{
    public string QueueName { get; set; } = "requests";
    public int RetryDelayMs { get; set; } = 5000;
    public int MaxMessagesPerPoll { get; set; } = 1;
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int EmptyQueueDelayMs { get; set; } = 2000;
    public int MaxDequeueCount { get; set; } = 5;
}