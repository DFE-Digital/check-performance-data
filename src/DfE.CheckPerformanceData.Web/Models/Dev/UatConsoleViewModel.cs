namespace DfE.CheckPerformanceData.Web.Models.Dev;

// The UAT console's render contract: the interactive runner rows, the ids of the automated-only
// coverage items, the current gating-flag state shown to the watcher, and the last driven
// reference for the journey shortcut. The coverage manifest itself is loaded client-side from the
// served uat-coverage.json so the panel can render its mapping without a server round-trip.
public sealed class UatConsoleViewModel
{
    public required IReadOnlyList<UatItem> Interactive { get; init; }
    public required IReadOnlyList<string> AutomatedCoverageIds { get; init; }
    public required bool DevToolsEnabled { get; init; }
    public required bool FakeZendesk { get; init; }
    public string? LastReference { get; init; }
}
