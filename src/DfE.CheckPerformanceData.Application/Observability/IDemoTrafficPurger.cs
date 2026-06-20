namespace DfE.CheckPerformanceData.Application.Observability;

// Removes synthetic demo traffic from the pipeline tables while leaving real submissions intact.
// The demo tooling mints references with well-known prefixes (drive / seed / inject / seed-DLQ),
// so a purge can target exactly those rows. Dev/test only — the only caller is the gated
// /dev/uat/purge-demo endpoint; it never runs in production.
public interface IDemoTrafficPurger
{
    Task<DemoPurgeResult> PurgeAsync(CancellationToken cancellationToken);
}

// The well-known reference prefixes the demo tooling mints. A real submission's reference never
// starts with one of these, so matching on them removes only fabricated traffic:
//   DEV-       DevPipelineRunner drive (Drive approved/rejected/scrutiny)
//   SEED-      PipelineMetricsSeeder (the "Seed messages" bulk history)
//   demo-fail- DevUat InjectFailure + DevQueueSeed InjectFailureDemo (demo-fail-{preset}-…)
//   uat-dlq-   DevUat SeedDlq
//   e2e-dlq-   DevQueueSeed SeedDeadLetter (also used by the E2E harness)
public static class DemoTrafficPrefixes
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "DEV-",
        "SEED-",
        "demo-fail-",
        "uat-dlq-",
        "e2e-dlq-",
    };
}

// How many rows the purge removed from each pipeline table, so the caller can report it.
public sealed record DemoPurgeResult(int MetricEvents, int DeadLetters, int QueueMessages)
{
    public int Total => MetricEvents + DeadLetters + QueueMessages;
}
