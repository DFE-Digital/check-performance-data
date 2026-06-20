namespace DfE.CheckPerformanceData.Application.Observability;

// The stages a message passes through, recorded as one metric row per stage. The two consumer
// completions supply RulesEvaluated and TicketCreated; DeadLettered records a message that
// exceeded its attempt cap. Submitted is the enqueue-time first step on the journey timeline,
// written by SubmittedMetricRecorder wherever the web side enqueues onto the rules-engine
// queue, so a request's timeline starts when it was submitted rather than at its first
// consumer ack. Stored as plain strings so the history survives any later enum reshaping.
public static class MetricStages
{
    public const string Submitted = "Submitted";
    public const string RulesEvaluated = "RulesEvaluated";
    public const string TicketCreated = "TicketCreated";
    public const string DeadLettered = "DeadLettered";

    // The known stages, in pipeline order — the allow-list the transactions stage filter validates
    // against so only a recorded stage value can reach the query.
    public static readonly IReadOnlyList<string> All = new[]
    {
        Submitted, RulesEvaluated, TicketCreated, DeadLettered,
    };
}
