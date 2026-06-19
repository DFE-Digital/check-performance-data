namespace DfE.CheckPerformanceData.Application.Observability;

// Maps a queue's vital signs and the configured thresholds to a renderable HealthState. Pure
// and side-effect free so the threshold-to-state mapping is fully unit-testable.
public interface IHealthEvaluator
{
    HealthState Evaluate(HealthInputs inputs, HealthThresholds thresholds);

    // Explains a non-flowing state: one reason per signal (depth, oldest age, dead-letter count)
    // that has reached at least its amber threshold, each tagged with the band it reaches and the
    // actual-vs-threshold figures. Empty when everything is below its thresholds. Pure, like
    // Evaluate, so the "why" is derived from the same inputs without a second data round-trip.
    IReadOnlyList<HealthReason> Explain(HealthInputs inputs, HealthThresholds thresholds);
}
