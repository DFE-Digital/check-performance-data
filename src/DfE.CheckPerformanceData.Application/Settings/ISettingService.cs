namespace DfE.CheckPerformanceData.Application.Settings;

// Settings read/write with code-declared defaults. Reads resolve the effective value
// (stored value, or the default when nothing is stored or the stored value is blank).
// Writes only accept keys declared in SettingDefinitions.
public interface ISettingService
{
    Task<List<SettingViewItem>> GetAllWithValuesAsync();
    Task<string> GetValueAsync(string key);
    Task<int> GetIntAsync(string key);
    Task SaveAsync(string key, string? value);
}
