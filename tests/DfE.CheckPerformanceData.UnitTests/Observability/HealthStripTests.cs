using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

// The traffic-light health strip maps queue depth, oldest-message age and the dead-letter
// rate to one of three states. Each state must carry colour AND a text label AND a shape
// token, so colour alone never decides the state (WCAG 1.4.1). Thresholds come from the
// Health:* settings; the evaluator is handed the resolved thresholds.
public sealed class HealthStripTests
{
    private readonly IHealthEvaluator _sut = new HealthEvaluator();

    private static HealthThresholds Thresholds() => new(
        DepthAmber: 10,
        DepthRed: 50,
        OldestAgeAmberSeconds: 60,
        OldestAgeRedSeconds: 300,
        DlqRateRed: 5,
        DlqRateAmber: 3);

    // --- Below every threshold → Flowing (green, "Flowing", filled circle) ---

    [Fact]
    public void Evaluate_BelowAllThresholds_IsFlowing()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 2, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 0),
            Thresholds());

        Assert.Equal(HealthLevel.Flowing, state.Level);
        Assert.Equal("#00703c", state.Colour);
        Assert.Equal("Flowing", state.Label);
        Assert.Equal("circle", state.ShapeToken);
    }

    // --- Depth over the amber threshold → Backing up (orange, "Backing up", half circle) ---

    [Fact]
    public void Evaluate_DepthOverAmber_IsBackingUp()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 20, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 0),
            Thresholds());

        Assert.Equal(HealthLevel.BackingUp, state.Level);
        Assert.Equal("#f47738", state.Colour);
        Assert.Equal("Backing up", state.Label);
        Assert.Equal("half-circle", state.ShapeToken);
    }

    // --- Oldest age over the amber threshold → Backing up ---

    [Fact]
    public void Evaluate_OldestAgeOverAmber_IsBackingUp()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(120), DeadLetterCount: 0),
            Thresholds());

        Assert.Equal(HealthLevel.BackingUp, state.Level);
    }

    // --- Depth over the red threshold → Needs attention (red, label, hollow ring) ---

    [Fact]
    public void Evaluate_DepthOverRed_IsNeedsAttention()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 80, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 0),
            Thresholds());

        Assert.Equal(HealthLevel.NeedsAttention, state.Level);
        Assert.Equal("#d4351c", state.Colour);
        Assert.Equal("Needs attention", state.Label);
        Assert.Equal("ring", state.ShapeToken);
    }

    // --- Oldest age over the red threshold → Needs attention (a stall) ---

    [Fact]
    public void Evaluate_OldestAgeOverRed_IsNeedsAttention()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(600), DeadLetterCount: 0),
            Thresholds());

        Assert.Equal(HealthLevel.NeedsAttention, state.Level);
    }

    // --- Dead-letter count over the red threshold → Needs attention (DLQ rising) ---

    [Fact]
    public void Evaluate_DeadLetterOverRed_IsNeedsAttention()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 9),
            Thresholds());

        Assert.Equal(HealthLevel.NeedsAttention, state.Level);
    }

    // --- Dead-letter count at the amber threshold (below red) → Backing up (a warning) ---

    [Fact]
    public void Evaluate_DeadLetterAtAmber_BelowRed_IsBackingUp()
    {
        // Amber 3, red 5: a count of 3 is a warning, not yet a failure.
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 3),
            Thresholds());

        Assert.Equal(HealthLevel.BackingUp, state.Level);
        Assert.Equal("#f47738", state.Colour); // the amber/warning colour
    }

    [Fact]
    public void Evaluate_DeadLetterBelowAmber_IsFlowing()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 2),
            Thresholds());

        Assert.Equal(HealthLevel.Flowing, state.Level);
    }

    // --- Red dominates amber: a queue both backing up and stalled reads Needs attention ---

    [Fact]
    public void Evaluate_RedTakesPrecedenceOverAmber()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 80, OldestAge: TimeSpan.FromSeconds(120), DeadLetterCount: 0),
            Thresholds());

        Assert.Equal(HealthLevel.NeedsAttention, state.Level);
    }

    // --- A null oldest age (empty queue) never trips the age thresholds ---

    [Fact]
    public void Evaluate_NullOldestAge_DoesNotTripAge()
    {
        var state = _sut.Evaluate(
            new HealthInputs(Depth: 0, OldestAge: null, DeadLetterCount: 0),
            Thresholds());

        Assert.Equal(HealthLevel.Flowing, state.Level);
    }

    // === Explain(): the "why" behind a non-green state ===========================
    // The strip must say which condition tripped and the actual-vs-threshold figures,
    // not just "needs attention". Explain returns one HealthReason per signal that has
    // reached at least its amber threshold, tagged with the band it reaches and the
    // preformatted actual / limit values.

    // --- Everything healthy → nothing to explain ---

    [Fact]
    public void Explain_BelowAllThresholds_IsEmpty()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 2, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 0),
            Thresholds());

        Assert.Empty(reasons);
    }

    // --- Depth at amber → one depth reason carrying the amber band and the amber limit ---

    [Fact]
    public void Explain_DepthAtAmber_NamesDepthAgainstAmberLimit()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 20, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 0),
            Thresholds());

        var reason = Assert.Single(reasons);
        Assert.Equal("Queue depth", reason.Signal);
        Assert.Equal(HealthLevel.BackingUp, reason.Band);
        Assert.Equal("20", reason.Actual);
        Assert.Equal("10", reason.Threshold);
    }

    // --- Depth at red → depth reason carrying the red band and the red limit ---

    [Fact]
    public void Explain_DepthAtRed_NamesDepthAgainstRedLimit()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 80, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 0),
            Thresholds());

        var reason = Assert.Single(reasons);
        Assert.Equal("Queue depth", reason.Signal);
        Assert.Equal(HealthLevel.NeedsAttention, reason.Band);
        Assert.Equal("80", reason.Actual);
        Assert.Equal("50", reason.Threshold);
    }

    // --- Oldest age over red → age reason with friendly durations (300s → "5m") ---

    [Fact]
    public void Explain_OldestAgeAtRed_NamesAgeWithFriendlyDurations()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(600), DeadLetterCount: 0),
            Thresholds());

        var reason = Assert.Single(reasons);
        Assert.Equal("Oldest message age", reason.Signal);
        Assert.Equal(HealthLevel.NeedsAttention, reason.Band);
        Assert.Equal("10m", reason.Actual);
        Assert.Equal("5m", reason.Threshold);
    }

    // --- A null oldest age contributes no age reason ---

    [Fact]
    public void Explain_NullOldestAge_HasNoAgeReason()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 0, OldestAge: null, DeadLetterCount: 0),
            Thresholds());

        Assert.DoesNotContain(reasons, r => r.Signal == "Oldest message age");
    }

    // --- Dead-letter over red → dead-letter reason (DLQ has a red threshold only) ---

    [Fact]
    public void Explain_DeadLetterAtRed_NamesDeadLetterCount()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 9),
            Thresholds());

        var reason = Assert.Single(reasons);
        Assert.Equal("Dead-letter messages", reason.Signal);
        Assert.Equal(HealthLevel.NeedsAttention, reason.Band);
        Assert.Equal("9", reason.Actual);
        Assert.Equal("5", reason.Threshold);
    }

    // --- Dead-letter at amber (below red) → dead-letter reason against the amber limit ---

    [Fact]
    public void Explain_DeadLetterAtAmber_NamesDeadLetterAgainstAmberLimit()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 1, OldestAge: TimeSpan.FromSeconds(5), DeadLetterCount: 3),
            Thresholds());

        var reason = Assert.Single(reasons);
        Assert.Equal("Dead-letter messages", reason.Signal);
        Assert.Equal(HealthLevel.BackingUp, reason.Band);
        Assert.Equal("3", reason.Actual);
        Assert.Equal("3", reason.Threshold);
    }

    // --- Several signals tripped → one reason each, every concerning signal surfaced ---

    [Fact]
    public void Explain_MultipleSignalsTripped_ReturnsOneReasonEach()
    {
        var reasons = _sut.Explain(
            new HealthInputs(Depth: 80, OldestAge: TimeSpan.FromSeconds(120), DeadLetterCount: 9),
            Thresholds());

        Assert.Equal(3, reasons.Count);
        Assert.Contains(reasons, r => r.Signal == "Queue depth" && r.Band == HealthLevel.NeedsAttention);
        Assert.Contains(reasons, r => r.Signal == "Oldest message age" && r.Band == HealthLevel.BackingUp);
        Assert.Contains(reasons, r => r.Signal == "Dead-letter messages" && r.Band == HealthLevel.NeedsAttention);
    }
}
