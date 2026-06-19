namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The canonical five pipeline stages, left to right, and the mapping from a recorded event's stage
// name to its board stage key. This mirrors the board JS stageKeyFor() exactly so the server-built
// replay walkthrough and the live board agree on where an item sits.
public static class PipelineStages
{
    public static readonly IReadOnlyList<(string Key, string Label)> Ordered = new[]
    {
        ("submit", "Submit"),
        ("rules-queue", "Rules queue"),
        ("rules-engine", "Rules engine"),
        ("zendesk-queue", "Zendesk queue"),
        ("ticket", "Ticket"),
    };

    public static readonly IReadOnlyList<string> OrderKeys =
        Ordered.Select(s => s.Key).ToList();

    // Maps a recorded event stage name (e.g. "Submitted", "RulesEvaluated", "TicketCreated") to a
    // board stage key. Kept in lock-step with the board JS so a single-stepped cohort lands on the
    // same node the live animation would.
    public static string KeyForStage(string? stage)
    {
        var s = (stage ?? string.Empty).ToLowerInvariant();
        if (s.Contains("submit")) return "submit";
        if (s.Contains("ticket")) return "ticket";
        if (s.Contains("zendesk")) return "zendesk-queue";
        if (s.Contains("rules")) return "rules-engine";
        return "rules-queue";
    }

    public static int IndexOfKey(string key)
    {
        var i = OrderKeys.ToList().IndexOf(key);
        return i < 0 ? 0 : i;
    }
}
