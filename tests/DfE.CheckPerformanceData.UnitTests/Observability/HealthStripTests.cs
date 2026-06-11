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
        DlqRateRed: 5);

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
}
