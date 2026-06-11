namespace DfE.CheckPerformanceData.Application.Observability;

// The stages a message passes through, recorded as one metric row per stage. The two consumer
// completions supply RulesEvaluated and TicketCreated; DeadLettered records a message that
// exceeded its attempt cap. Submitted is reserved for an enqueue-time first step on the journey
// timeline: there is no production enqueue path onto the rules-engine queue yet (only the dev
// pipeline tooling enqueues), so nothing records it today — it exists so the timeline gains its
// first step automatically once a real submit-to-queue path is wired. Stored as plain strings
// so the history survives any later enum reshaping.
public static class MetricStages
{
    public const string Submitted = "Submitted";
    public const string RulesEvaluated = "RulesEvaluated";
    public const string TicketCreated = "TicketCreated";
    public const string DeadLettered = "DeadLettered";
}
