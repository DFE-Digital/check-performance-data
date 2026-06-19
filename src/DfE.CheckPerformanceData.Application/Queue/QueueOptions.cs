namespace DfE.CheckPerformanceData.Application.Queue;

public sealed class QueueOptions
{
    public const string RulesEngineQueue = "rules-engine";
    public const string ZendeskQueue = "zendesk";

    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // Counts CLAIMS, not failures: every dequeue increments a message's attempt count, including
    // a re-claim after the visibility timeout re-surfaced a still-running message. A message is
    // dead-lettered only when its processing actually throws at or beyond this cap — a message
    // that merely times out without failing is never dead-lettered. Size VisibilityTimeout safely
    // above the worst-case processing time so slow-but-successful work does not burn the budget.
    public int MaxAttempts { get; set; } = 5;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
