namespace DfE.CheckPerformanceData.Application.Observability;

// Maps a queue's vital signs and the configured thresholds to a renderable HealthState. Pure
// and side-effect free so the threshold-to-state mapping is fully unit-testable.
public interface IHealthEvaluator
{
    HealthState Evaluate(HealthInputs inputs, HealthThresholds thresholds);
}
