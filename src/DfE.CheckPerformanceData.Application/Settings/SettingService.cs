namespace DfE.CheckPerformanceData.Application.Settings;

public sealed class SettingService(ISettingRepository repository) : ISettingService
{
    public async Task<List<SettingViewItem>> GetAllWithValuesAsync()
    {
        var stored = await repository.GetAllAsync();

        return SettingDefinitions.All.Select(d =>
        {
            var hasStored = stored.TryGetValue(d.Key, out var value) && !string.IsNullOrWhiteSpace(value);
            return new SettingViewItem(
                Key: d.Key,
                Description: d.Description,
                Value: hasStored ? value! : d.DefaultValue,
                DefaultValue: d.DefaultValue,
                IsDefault: !hasStored,
                Kind: d.Kind);
        }).ToList();
    }

    public async Task<string> GetValueAsync(string key)
    {
        var definition = Require(key);
        var stored = await repository.GetValueAsync(key);
        return string.IsNullOrWhiteSpace(stored) ? definition.DefaultValue : stored!;
    }

    public async Task<int> GetIntAsync(string key)
    {
        var raw = await GetValueAsync(key);
        return int.TryParse(raw, out var parsed)
            ? parsed
            : int.Parse(Require(key).DefaultValue);
    }

    public async Task<double> GetDoubleAsync(string key)
    {
        var raw = await GetValueAsync(key);
        // InvariantCulture on the parse + the fallback so a "0.10" stored on one host
        // reads identically on a comma-decimal locale. GetValueAsync already resolves
        // the code-declared default when nothing is stored, so raw is never null.
        return double.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            ? parsed
            : double.Parse(Require(key).DefaultValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> GetBoolAsync(string key)
    {
        var definition = Require(key);
        var stored = await repository.GetValueAsync(key);
        // Unset / blank stored value falls straight back to the code-declared default so a
        // freshly provisioned environment behaves the same as one with the value cleared.
        // For non-blank stored values, bool.TryParse handles case-insensitive true/false;
        // anything else is treated as garbage and also falls back to the default rather
        // than being silently coerced to false.
        if (string.IsNullOrWhiteSpace(stored))
            return bool.Parse(definition.DefaultValue);

        return bool.TryParse(stored, out var parsed)
            ? parsed
            : bool.Parse(definition.DefaultValue);
    }

    public async Task SaveAsync(string key, string? value)
    {
        _ = Require(key); // reject unknown keys before touching the store

        if (string.IsNullOrWhiteSpace(value))
            await repository.DeleteAsync(key); // clearing reverts to the code-declared default
        else
            await repository.UpsertAsync(key, value.Trim());
    }

    private static SettingDefinition Require(string key) =>
        SettingDefinitions.Find(key)
        ?? throw new InvalidOperationException($"Unknown setting '{key}'.");
}
