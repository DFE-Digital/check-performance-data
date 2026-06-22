using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The single source of truth for the colour of each decision outcome across every chart on the
// dashboard (the decision-mix pie, the decision-mix-over-time lines) and the one shared legend
// beneath them. Centralising the map here means the pie, the over-time chart and the legend can
// never drift onto divergent hex literals: they all read these GDS palette colours. Colour is
// never the only signal — each chart keeps its aria-label and paired data table, and the legend
// states the colour→label meaning once in text.
public static class DecisionColours
{
    // GDS palette: green = auto-approved (good), red = auto-rejected, orange = needs human scrutiny.
    public const string AutoApproved = "#00703c";
    public const string AutoRejected = "#d4351c";
    public const string Scrutiny = "#f47738";

    // The fallback for any status not in the map (a future outcome, or a typo upstream): GDS dark
    // grey, so an unmapped slice/line is still drawn rather than vanishing.
    public const string Fallback = "#505a5f";

    // The known statuses keyed case-insensitively. "RequiresScrutiny" is the persisted status name
    // for the human-scrutiny outcome; it shares the Scrutiny colour.
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["AutoApproved"] = AutoApproved,
            ["AutoRejected"] = AutoRejected,
            ["RequiresScrutiny"] = Scrutiny,
            ["Scrutiny"] = Scrutiny,
        };

    // The colour for a decision status, falling back to GDS dark grey for anything unmapped.
    public static string For(string status) =>
        status is not null && Map.TryGetValue(status, out var colour) ? colour : Fallback;

    // The legend rows: the human-facing label paired with its colour, in reading order
    // (approved, rejected, scrutiny). The one shared legend partial renders these.
    public static IReadOnlyList<(string Label, string Colour)> LegendItems { get; } = new[]
    {
        ("Auto-approved", AutoApproved),
        ("Auto-rejected", AutoRejected),
        ("Requires scrutiny", Scrutiny),
    };
}
