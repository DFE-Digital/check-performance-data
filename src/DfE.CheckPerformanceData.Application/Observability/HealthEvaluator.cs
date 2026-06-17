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

    public IReadOnlyList<HealthReason> Explain(HealthInputs inputs, HealthThresholds thresholds)
    {
        var reasons = new List<HealthReason>();
        var ageSeconds = inputs.OldestAge?.TotalSeconds ?? 0d;

        // Depth: report against whichever limit it has reached, red first so the figure shown is
        // the one that actually decided the band.
        if (inputs.Depth >= thresholds.DepthRed)
            reasons.Add(new HealthReason(
                "Queue depth", HealthLevel.NeedsAttention,
                inputs.Depth.ToString(), thresholds.DepthRed.ToString()));
        else if (inputs.Depth >= thresholds.DepthAmber)
            reasons.Add(new HealthReason(
                "Queue depth", HealthLevel.BackingUp,
                inputs.Depth.ToString(), thresholds.DepthAmber.ToString()));

        // Oldest message age: only when the queue has a waiting message (null age = empty queue).
        if (inputs.OldestAge is { } age)
        {
            if (ageSeconds >= thresholds.OldestAgeRedSeconds)
                reasons.Add(new HealthReason(
                    "Oldest message age", HealthLevel.NeedsAttention,
                    FormatDuration(age),
                    FormatDuration(TimeSpan.FromSeconds(thresholds.OldestAgeRedSeconds))));
            else if (ageSeconds >= thresholds.OldestAgeAmberSeconds)
                reasons.Add(new HealthReason(
                    "Oldest message age", HealthLevel.BackingUp,
                    FormatDuration(age),
                    FormatDuration(TimeSpan.FromSeconds(thresholds.OldestAgeAmberSeconds))));
        }

        // Dead-letter count has a red threshold only — any dead-lettering is treated as attention,
        // never merely "backing up".
        if (inputs.DeadLetterCount >= thresholds.DlqRateRed)
            reasons.Add(new HealthReason(
                "Dead-letter messages", HealthLevel.NeedsAttention,
                inputs.DeadLetterCount.ToString(), thresholds.DlqRateRed.ToString()));

        return reasons;
    }

    // Compact, human-friendly duration: seconds under a minute, whole minutes (with a trailing
    // seconds part only when non-zero), or hours-and-minutes above an hour. Keeps the "why" line
    // readable ("10m" not "600 seconds") while staying exact.
    private static string FormatDuration(TimeSpan span)
    {
        var total = (int)Math.Round(span.TotalSeconds);
        if (total < 60)
            return $"{total}s";

        if (total < 3600)
        {
            var minutes = total / 60;
            var seconds = total % 60;
            return seconds == 0 ? $"{minutes}m" : $"{minutes}m {seconds}s";
        }

        var hours = total / 3600;
        var remMinutes = total % 3600 / 60;
        return remMinutes == 0 ? $"{hours}h" : $"{hours}h {remMinutes}m";
    }
}
