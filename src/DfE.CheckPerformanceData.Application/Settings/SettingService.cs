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
                IsDefault: !hasStored);
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
