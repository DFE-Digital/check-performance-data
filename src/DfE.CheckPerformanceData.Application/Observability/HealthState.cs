namespace DfE.CheckPerformanceData.Application.Observability;

// The three health bands the traffic-light strip maps a queue's vital signs onto.
public enum HealthLevel
{
    Flowing,
    BackingUp,
    NeedsAttention,
}

// A resolved health band ready to render: the GDS colour, the text label, and a non-colour
// shape token. All three are carried so the state is conveyed by colour AND text AND shape and
// never by colour alone (WCAG 1.4.1, use of colour).
public sealed record HealthState(HealthLevel Level, string Colour, string Label, string ShapeToken);

// The signals one queue (or the overall pipeline) presents at evaluation time: its current
// depth, the age of its oldest waiting message (null when empty), and the count of dead-lettered
// messages.
public sealed record HealthInputs(int Depth, TimeSpan? OldestAge, int DeadLetterCount);

// The depth / age / dead-letter thresholds that separate the bands. Resolved from the Health:*
// settings (with code defaults), then handed to the evaluator so the mapping itself stays pure.
public sealed record HealthThresholds(
    int DepthAmber,
    int DepthRed,
    int OldestAgeAmberSeconds,
    int OldestAgeRedSeconds,
    int DlqRateRed);
