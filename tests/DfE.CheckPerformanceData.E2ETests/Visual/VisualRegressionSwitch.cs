namespace DfE.CheckPerformanceData.E2ETests.Visual;

/// <summary>
/// Opt-in switch for the visual-regression comparisons. Off unless explicitly asked for.
/// </summary>
/// <remarks>
/// A screenshot comparison is only meaningful against baselines captured in the pinned
/// Playwright container. Run from anywhere else — a developer machine, a dev container —
/// and the images differ on font rasterisation and DPI alone, so the run reports failures
/// that say nothing about the code and, worse, rewrites the committed baseline PNGs on
/// disk as a side effect. A later container run then compares against those corrupted
/// baselines, and the damage is invisible until someone reads the diff.
///
/// Nothing depends on these checks: the deploy workflow already excludes them by category,
/// so they have only ever run when invoked by hand. Gating them behind an environment
/// variable makes "off" the behaviour everywhere — including a bare
/// <c>dotnet test</c> on the project, which no category filter covers — while keeping the
/// tests and their baselines in place for whoever wants to run them deliberately.
/// </remarks>
internal static class VisualRegressionSwitch
{
    internal const string EnvironmentVariable = "CPD_E2E_VISUAL_REGRESSION";

    internal const string SkipReason =
        "Visual regression is off by default. Set " + EnvironmentVariable +
        "=1 and run inside the Playwright container to compare against the committed baselines.";

    /// <summary>
    /// True only when the switch is set to <c>1</c> or <c>true</c>. Any other value — including
    /// unset, empty, or an accidental <c>0</c> — leaves the comparisons off.
    /// </summary>
    internal static bool Enabled =>
        Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?.Trim() is "1" or "true" or "TRUE" or "True";
}
