namespace DfE.CheckPerformanceData.Application.Settings;

// Stable keys for the settings known to the application. Settings not declared here are
// rejected on save, so the settings form cannot be used to write arbitrary key/value rows.
public static class SettingKeys
{
    public const string WikiPageLength = "Wiki:PageLength";

    public const string DlqFullPayloadEnabled = "Dlq:FullPayloadEnabled";
    public const string DlqAlertThreshold = "Dlq:AlertThreshold";
    public const string DlqAlertRecipients = "Dlq:AlertRecipients";
    public const string DlqRetentionDays = "Dlq:RetentionDays";

    public const string MetricsRetentionDays = "Metrics:RetentionDays";
    public const string MetricsRetentionIntervalMinutes = "Metrics:RetentionIntervalMinutes";
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
        new(SettingKeys.WikiPageLength,
            "Number of rows shown per page on paged lists, such as the deleted pages list.",
            "20",
            SettingKind.Int),
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
            SettingKind.Int)
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
