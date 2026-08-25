namespace DfE.CheckPerformanceData.Application.Settings;

// Stable keys for the settings known to the application. Settings not declared here are
// rejected on save, so the settings form cannot be used to write arbitrary key/value rows.
public static class SettingKeys
{
    public const string CmsPageLength = "CMS:PageLength";
    public const string SearchDebugOn = "CMS:SearchDebugOn";
    public const string ShowDeleteAllButton = "CMS:ShowDeleteAllButton";

    public const string DlqFullPayloadEnabled = "Dlq:FullPayloadEnabled";
    public const string DlqAlertThreshold = "Dlq:AlertThreshold";
    public const string DlqAlertRecipients = "Dlq:AlertRecipients";
    public const string DlqRetentionDays = "Dlq:RetentionDays";

    public const string MetricsRetentionDays = "Metrics:RetentionDays";
    public const string MetricsRetentionIntervalMinutes = "Metrics:RetentionIntervalMinutes";

    public const string SearchAnalyticsSessionIdleMinutes = "SearchAnalytics:SessionIdleMinutes";
    public const string SearchAnalyticsSessionAbsoluteHours = "SearchAnalytics:SessionAbsoluteHours";
    public const string SearchAnalyticsShowSessionComment = "SearchAnalytics:ShowSessionComment";
    public const string SearchAnalyticsRetentionDays = "SearchAnalytics:RetentionDays";
    public const string SearchAnalyticsRetentionIntervalMinutes = "SearchAnalytics:RetentionIntervalMinutes";
    public const string SearchAnalyticsMessageRetentionDays = "SearchAnalytics:MessageRetentionDays";
    public const string SearchAnalyticsSeedSecondsPerEvent = "SearchAnalytics:SeedSecondsPerEvent";

    public const string HealthDepthAmber = "Health:DepthAmber";
    public const string HealthDepthRed = "Health:DepthRed";
    public const string HealthOldestAgeAmberSeconds = "Health:OldestAgeAmberSeconds";
    public const string HealthOldestAgeRedSeconds = "Health:OldestAgeRedSeconds";
    public const string HealthDlqRateRed = "Health:DlqRateRed";

    public const string DevToolsEnabled = "Dev:ToolsEnabled";
    public const string ZendeskUseFake = "Zendesk:UseFake";
    public const string NotifyUseFake = "Notify:UseFake";
}

// The data type of a setting's value, used by the settings editor to choose how to render
// the field (text input for String/Int, checkbox for Bool) and how to interpret the post.
public enum SettingKind
{
    String,
    Int,
    Bool
}

// A setting the application understands: its key, an editor-facing description, the value
// used when nothing is stored, and its value kind. Defaults live in code so a fresh
// environment behaves correctly before anyone visits the settings page. Kind defaults to
// String so existing call sites stay valid.
public sealed record SettingDefinition(
    string Key,
    string Description,
    string DefaultValue,
    SettingKind Kind = SettingKind.String);

// The single source of truth for which settings exist and their defaults.
public static class SettingDefinitions
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(SettingKeys.CmsPageLength,
            "Number of rows shown per page on paged lists.",
            "20",
            SettingKind.Int),
        new(SettingKeys.ShowDeleteAllButton,
            "Whether the content-staging page offers the Clear all CMS content button, which deletes " +
            "every page and content block in this environment. Off by default, and unavailable in " +
            "production and on QA and preproduction whatever this is set to.",
            "false",
            SettingKind.Bool),
        new(SettingKeys.DlqFullPayloadEnabled,
            "Whether operators may view the full original payload of a dead-lettered message. Off by default.",
            "false",
            SettingKind.Bool),
        new(SettingKeys.DlqAlertThreshold,
            "Dead-letter queue depth at which an email alert is raised to the configured recipients.",
            "10",
            SettingKind.Int),
        new(SettingKeys.DlqAlertRecipients,
            "Comma-separated email addresses notified when the dead-letter queue crosses the alert threshold.",
            "",
            SettingKind.String),
        new(SettingKeys.DlqRetentionDays,
            "Number of days a dead-lettered message is retained before it is purged. The admin-action audit trail is kept independently.",
            "90",
            SettingKind.Int),
        new(SettingKeys.MetricsRetentionDays,
            "Number of days a queue metrics event row is retained before it is purged.",
            "30",
            SettingKind.Int),
        new(SettingKeys.MetricsRetentionIntervalMinutes,
            "How often, in minutes, the metrics retention job runs to purge expired event rows.",
            "60",
            SettingKind.Int),
        new(SettingKeys.HealthDepthAmber,
            "Queue depth at which the health strip turns amber (backing up).",
            "25",
            SettingKind.Int),
        new(SettingKeys.HealthDepthRed,
            "Queue depth at which the health strip turns red (needs attention).",
            "100",
            SettingKind.Int),
        new(SettingKeys.HealthOldestAgeAmberSeconds,
            "Age in seconds of the oldest waiting message at which the health strip turns amber.",
            "120",
            SettingKind.Int),
        new(SettingKeys.HealthOldestAgeRedSeconds,
            "Age in seconds of the oldest waiting message at which the health strip turns red (a stall).",
            "600",
            SettingKind.Int),
        new(SettingKeys.HealthDlqRateRed,
            "Dead-letter queue count at which the health strip turns red (the dead-letter queue is rising).",
            "5",
            SettingKind.Int),
        new(SettingKeys.NotifyUseFake,
            "When true, the Notify email service is replaced with a dev-only fake that logs email details to the console instead of sending real emails. Defaults to true when not configured.",
            "true",
            SettingKind.Bool),
        new(SettingKeys.SearchDebugOn,
            "When true, per-hit rank breakdowns and per-exclusion filter breadcrumbs are logged at Info level and the /search page renders the internal rank as an HTML comment. Editors and ops use it to debug why a page didn't appear. Off by default.",
            "false",
            SettingKind.Bool),
        // SearchAnalytics:SessionIdleMinutes is deliberately NOT declared here. It is
        // applied to SessionOptions when the request pipeline is built and the framework
        // reads it once, so nothing can honour a stored value per request. Listing it as
        // editable produced a control that saved and redisplayed but changed nothing —
        // configure it in appsettings instead.
        new(SettingKeys.SearchAnalyticsSessionAbsoluteHours,
            "The maximum lifetime of a session even under continuous activity. A replayed cookie past this limit gets a fresh session id. Hard max 168 h (1 week) enforced in code.",
            "24",
            SettingKind.Int),
        new(SettingKeys.SearchAnalyticsShowSessionComment,
            "Emit an HTML comment with the ASP.NET session id at the tail of every text/html response for support diagnosis. Turn off only in hostile environments where visible session ids are unacceptable.",
            "true",
            SettingKind.Bool),
        new(SettingKeys.SearchAnalyticsRetentionDays,
            "Number of days a search-events row is retained before it is purged by the nightly job. Hard-max 365 days is enforced in code.",
            "90",
            SettingKind.Int),
        new(SettingKeys.SearchAnalyticsRetentionIntervalMinutes,
            "How often, in minutes, the search-analytics retention job runs to purge expired rows.",
            "60",
            SettingKind.Int),
        new(SettingKeys.SearchAnalyticsMessageRetentionDays,
            "Number of days a search-messages row (user feedback message) is retained before it is purged. Longer than the events window because support cases often reference weeks-old sessions. Hard-max 730 days is enforced in code.",
            "365",
            SettingKind.Int),
        new(SettingKeys.SearchAnalyticsSeedSecondsPerEvent,
            "Per-event throughput estimate (seconds) used to render the initial ETA on the dev-only Seed sample search data modal. Updated with an EMA blend after each non-cancelled seed so it converges on the actual host's throughput. Default is deliberately high so first-run estimates look conservative rather than optimistic.",
            "0.1",
            SettingKind.String)
    ];

    public static SettingDefinition? Find(string key) =>
        All.FirstOrDefault(d => d.Key == key);
}

// A known setting paired with its effective value for display on the settings page.
// IsDefault is true when no value is stored and the code default is being used.
public sealed record SettingViewItem(
    string Key,
    string Description,
    string Value,
    string DefaultValue,
    bool IsDefault,
    SettingKind Kind = SettingKind.String);
