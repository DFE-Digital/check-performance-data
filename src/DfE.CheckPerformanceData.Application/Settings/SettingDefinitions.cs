namespace DfE.CheckPerformanceData.Application.Settings;

// Stable keys for the settings known to the application. Settings not declared here are
// rejected on save, so the settings form cannot be used to write arbitrary key/value rows.
public static class SettingKeys
{
    public const string WikiPageLength = "Wiki:PageLength";
}

// A setting the application understands: its key, an editor-facing description, and the
// value used when nothing is stored. Defaults live in code so a fresh environment behaves
// correctly before anyone visits the settings page.
public sealed record SettingDefinition(string Key, string Description, string DefaultValue);

// The single source of truth for which settings exist and their defaults.
public static class SettingDefinitions
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(SettingKeys.WikiPageLength,
            "Number of rows shown per page on paged lists, such as the deleted pages list.",
            "20")
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
    bool IsDefault);
