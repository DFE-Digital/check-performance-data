namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// AB#302158: when the service hands a checking exercise's requests over for processing by
/// itself. Bound from the <c>ExerciseHandOver</c> configuration section; the defaults are the
/// ticket's, so no environment needs to set anything. An environment can switch the job off with
/// <c>ExerciseHandOver__Enabled=false</c> in <c>terraform/application/config/{env}.yml</c>.
/// </summary>
/// <remarks>
/// A value that makes no sense falls back to its default rather than being obeyed: a zero poll
/// interval would spin, a negative delay would sweep an exercise that is still open, and a zero
/// catch-up window would mean the job never runs.
/// </remarks>
public sealed record ExerciseHandOverSettings
{
    public const string SectionName = "ExerciseHandOver";

    public static readonly TimeSpan DefaultDelayAfterEnd = TimeSpan.FromHours(2);
    public static readonly TimeSpan DefaultCatchUpWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(5);

    public bool Enabled { get; init; } = true;

    /// <summary>How long after an exercise's end date the first automatic hand-over runs.</summary>
    public TimeSpan DelayAfterEnd { get; init; } = DefaultDelayAfterEnd;

    /// <summary>
    /// How long, from that first run, the job keeps repeating the hand-over. The repeats are the
    /// retries — after a failure, and for any request an earlier run had to leave behind.
    /// </summary>
    public TimeSpan CatchUpWindow { get; init; } = DefaultCatchUpWindow;

    /// <summary>How often the job looks for due exercises.</summary>
    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;

    public TimeSpan EffectiveDelayAfterEnd =>
        DelayAfterEnd < TimeSpan.Zero ? DefaultDelayAfterEnd : DelayAfterEnd;

    public TimeSpan EffectiveCatchUpWindow =>
        CatchUpWindow <= TimeSpan.Zero ? DefaultCatchUpWindow : CatchUpWindow;

    public TimeSpan EffectivePollInterval =>
        PollInterval <= TimeSpan.Zero ? DefaultPollInterval : PollInterval;
}
