namespace DfE.CheckPerformanceData.Application.Settings;

// Persistence of arbitrary key/value settings. A setting is absent from the store until
// it is explicitly saved; deleting it reverts the effective value to the code-declared
// default (resolved in SettingService, not here).
public interface ISettingRepository
{
    Task<Dictionary<string, string>> GetAllAsync();
    Task<string?> GetValueAsync(string key);
    Task UpsertAsync(string key, string value);
    Task DeleteAsync(string key);
}
