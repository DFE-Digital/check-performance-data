namespace DfE.CheckPerformanceData.Application.Observability;

// Maps queue depth, oldest-message age and the dead-letter count to one of three health bands.
// Red (needs attention) dominates amber (backing up), which dominates green (flowing): the
// worst signal wins, so a queue that is both deep and stalled never reads as merely backing up.
// Every band carries a GDS colour, a text label and a shape token so the strip conveys state by
// colour AND text AND shape, never colour alone.
public sealed class HealthEvaluator : IHealthEvaluator
{
    private static readonly HealthState Flowing =
        new(HealthLevel.Flowing, "#00703c", "Flowing", "circle");

    private static readonly HealthState BackingUp =
        new(HealthLevel.BackingUp, "#f47738", "Backing up", "half-circle");

    private static readonly HealthState NeedsAttention =
        new(HealthLevel.NeedsAttention, "#d4351c", "Needs attention", "ring");

    public HealthState Evaluate(HealthInputs inputs, HealthThresholds thresholds)
    {
        var ageSeconds = inputs.OldestAge?.TotalSeconds ?? 0d;

        var red =
            inputs.Depth >= thresholds.DepthRed ||
            ageSeconds >= thresholds.OldestAgeRedSeconds ||
            inputs.DeadLetterCount >= thresholds.DlqRateRed;
        if (red)
            return NeedsAttention;

        var amber =
            inputs.Depth >= thresholds.DepthAmber ||
            ageSeconds >= thresholds.OldestAgeAmberSeconds;
        if (amber)
            return BackingUp;

        return Flowing;
    }
}
