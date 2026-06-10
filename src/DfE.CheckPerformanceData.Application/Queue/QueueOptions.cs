namespace DfE.CheckPerformanceData.Application.Queue;

public sealed class QueueOptions
{
    public const string RulesEngineQueue = "rules-engine";
    public const string ZendeskQueue = "zendesk";

    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public int ZendeskConcurrencyCap { get; set; } = 1;
}
