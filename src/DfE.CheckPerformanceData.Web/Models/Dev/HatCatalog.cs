namespace DfE.CheckPerformanceData.Web.Models.Dev;

// The encoded HAT inventory: every human-checkable item from 03.10-UAT.md + 03.13-UAT.md as a
// typed list (the guided runner), plus the ids of the automated-only items the coverage panel
// resolves against hat-coverage.json / hat-status.json. Encoding the inventory in code keeps the
// console honest — the runner and the coverage panel are generated from this list, not hand-kept.
public static class HatCatalog
{
    // Drive-traffic and failure endpoints reuse the existing dev controllers; the HAT POST drivers
    // post to the /dev/hat/* actions which delegate to those same paths and redirect back.
    private const string DriveApproved = "/dev/hat/drive?outcome=approved";
    private const string DriveRejected = "/dev/hat/drive?outcome=rejected";
    private const string DriveScrutiny = "/dev/hat/drive?outcome=scrutiny";
    private const string InjectFailure = "/dev/hat/inject-failure";
    private const string SeedDlq = "/dev/hat/seed-dlq";

    private const string Dashboard = "/admin/observability";
    private const string Queues = "/admin/queues";
    private const string Dlq = "/admin/queues/dlq";
    private const string Outbox = "/dev/zendesk/outbox";
    private const string Share = "/admin/share";

    // The interactive runner rows. Ordered 03.13 strands first (the showpiece tour), then the
    // 03.10 [UI] success criteria, matching the UI-SPEC item inventory tables.
    public static readonly IReadOnlyList<HatItem> Interactive = new List<HatItem>
    {
        new("3.13-1", HatPhase.Phase313, "Debug menu present and gated",
            "The Debug nav entry is visible here; with Dev:ToolsEnabled off the /dev/* surfaces 404.",
            new[] { Link("Open dashboard", Dashboard) }),

        new("3.13-2", HatPhase.Phase313, "Zendesk-styled ticket preview",
            "The outbox renders a faithful Zendesk-styled ticket, not raw JSON.",
            new[] { Link("Open outbox", Outbox) }, Outbox),

        new("3.13-3", HatPhase.Phase313, "Failure-and-recovery demo",
            "Inject a failing message, watch it go amber and land in the DLQ, then redrive it back to green.",
            new[] { Post("Inject failing message", InjectFailure), Link("Open DLQ", Dlq) }, Dlq),

        new("3.13-4", HatPhase.Phase313, "Queues page redesign",
            "Three queue panels with top-5 messages and a view-all link.",
            new[] { Link("Open queues", Queues) }, Queues),

        new("3.13-5", HatPhase.Phase313, "Live animated board",
            "Driving traffic animates tokens left to right across the board above.",
            new[] { Post("Drive approved", DriveApproved), Post("Drive rejected", DriveRejected), Post("Drive scrutiny", DriveScrutiny) }),

        new("3.13-6", HatPhase.Phase313, "Stats dashboard",
            "Driving traffic populates the dashboard charts.",
            new[] { Post("Drive approved", DriveApproved), Link("Open dashboard", Dashboard) }, Dashboard),

        new("3.13-8", HatPhase.Phase313, "Health strip",
            "The dashboard shows a per-queue and overall traffic-light health strip.",
            new[] { Link("Open dashboard", Dashboard) }, Dashboard),

        new("3.13-9", HatPhase.Phase313, "Plain-English status sentence",
            "The dashboard shows a generated plain-English status sentence.",
            new[] { Link("Open dashboard", Dashboard) }, Dashboard),

        new("3.13-10", HatPhase.Phase313, "Track-my-request journey",
            "Open the journey for the last driven reference and see Submitted to Ticket steps.",
            new[] { Post("Drive approved", DriveApproved), Link("Open journey for last reference", "/dev/hat/last-journey") }),

        new("3.13-11", HatPhase.Phase313, "Deploy / rules-version markers",
            "The throughput chart shows dashed deploy / rules-version markers.",
            new[] { Link("Open dashboard", Dashboard) }, Dashboard),

        new("3.13-12", HatPhase.Phase313, "Export this view",
            "The dashboard exports as a PNG or PDF download.",
            new[] { Link("Open dashboard", Dashboard) }, Dashboard),

        new("3.13-13", HatPhase.Phase313, "Shareable anonymised link",
            "A generated share link opens with no admin chrome and no pupil data.",
            new[] { Link("Open share admin", Share) }, Share),

        new("3.13-14", HatPhase.Phase313, "Wallboard mode",
            "The wallboard opens chrome-less with large type.",
            new[] { Link("Open share admin", Share) }, Share),

        new("3.13-15", HatPhase.Phase313, "Board replay",
            "The board replay scrubber re-animates recorded history.",
            new[] { Link("Open dashboard", Dashboard) }, Dashboard),

        new("3.13-A", HatPhase.Phase313, "Accessibility",
            "The dashboard is tab-reachable and every chart ships a paired data table.",
            new[] { Link("Open dashboard", Dashboard) }, Dashboard),

        new("3.13-G", HatPhase.Phase313, "Gating flags",
            "The current Dev:ToolsEnabled and Zendesk:UseFake state is shown; fake Zendesk stays on for dev/test.",
            Array.Empty<HatAction>()),

        new("3.10-4", HatPhase.Phase310, "Real DLQ, not silent delete",
            "An injected failing message appears in the DLQ with a reason and attempt count.",
            new[] { Post("Inject failing message", InjectFailure), Link("Open DLQ", Dlq) }, Dlq),

        new("3.10-7", HatPhase.Phase310, "Rules consumer persists decision",
            "Driving an approved request records RulesEvaluated plus the decision on its journey.",
            new[] { Post("Drive approved", DriveApproved), Link("Open journey for last reference", "/dev/hat/last-journey") }),

        new("3.10-11", HatPhase.Phase310, "Rules behaviour preserved",
            "Each preset lands its expected decision.",
            new[] { Post("Drive approved", DriveApproved), Post("Drive rejected", DriveRejected), Post("Drive scrutiny", DriveScrutiny) }),

        new("3.10-13", HatPhase.Phase310, "Queue list depth and age, manual refresh",
            "The queues page shows depth and oldest-message age with a Refresh button.",
            new[] { Link("Open queues", Queues) }, Queues),

        new("3.10-14", HatPhase.Phase310, "Redacted-by-default, audited full payload",
            "A message renders redacted by default; enabling Dlq:FullPayloadEnabled audits the full payload.",
            new[] { Link("Open DLQ", Dlq) }, Dlq),

        new("3.10-15", HatPhase.Phase310, "Top-bar DLQ badge",
            "The admin chrome shows a DLQ badge linking to the dead-letter queue.",
            new[] { Link("Open DLQ", Dlq) }, Dlq),

        new("3.10-16", HatPhase.Phase310, "Audited redrive and purge with confirm modal",
            "Redrive and purge are behind a GDS confirm modal and are audited.",
            new[] { Post("Seed DLQ", SeedDlq), Link("Open DLQ", Dlq) }, Dlq),

        new("3.10-18", HatPhase.Phase310, "Auth gating",
            "An anonymous request is challenged via OIDC; a non-admin is denied.",
            new[] { Link("Open queues", Queues) }, Queues),
    };

    // The automated-only items. Each id has a hat-coverage.json entry mapping it to a test project
    // and --filter; the coverage panel renders these read-only against hat-status.json.
    public static readonly IReadOnlyList<string> AutomatedCoverageIds = new[]
    {
        "3.10-1", "3.10-2", "3.10-3", "3.10-5", "3.10-6", "3.10-8", "3.10-10",
        "3.10-12", "3.10-17", "3.13-7", "3.10-pii", "3.10-audit",
    };

    private static HatAction Link(string label, string url) => new(label, url, HatActionMethod.Link);
    private static HatAction Post(string label, string url) => new(label, url, HatActionMethod.Post);
}
