namespace DfE.CheckPerformanceData.Application.Observability;

// The stages a message passes through, recorded as one metric row per stage. A synthetic
// "Submitted" event marks enqueue time so the journey timeline has a first step; the two
// consumer completions supply RulesEvaluated and TicketCreated; DeadLettered records a
// message that exceeded its attempt cap. Stored as plain strings so the history survives
// any later enum reshaping.
public static class MetricStages
{
    public const string Submitted = "Submitted";
    public const string RulesEvaluated = "RulesEvaluated";
    public const string TicketCreated = "TicketCreated";
    public const string DeadLettered = "DeadLettered";
}
