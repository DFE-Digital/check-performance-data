using System.Globalization;
using DfE.CheckPerformanceData.Application.Settings;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

// Minimal ISettingService stand-in for middleware tests that need to prove a DB-backed
// admin setting actually reaches the code that consumes it. Holds a handful of key/value
// pairs; anything not seeded resolves to the code-declared default, mirroring the real
// service's "stored value, else default" contract.
public sealed class StubSettingService : ISettingService
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public StubSettingService(string key, object value)
    {
        Set(key, value);
    }

    public StubSettingService Set(string key, object value)
    {
        _values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return this;
    }

    public Task<string> GetValueAsync(string key) =>
        Task.FromResult(_values.TryGetValue(key, out var v) ? v : DefaultFor(key));

    public Task<int> GetIntAsync(string key) =>
        Task.FromResult(int.Parse(Resolve(key), CultureInfo.InvariantCulture));

    public Task<double> GetDoubleAsync(string key) =>
        Task.FromResult(double.Parse(Resolve(key), CultureInfo.InvariantCulture));

    public Task<bool> GetBoolAsync(string key) =>
        Task.FromResult(bool.Parse(Resolve(key)));

    public Task<List<SettingViewItem>> GetAllWithValuesAsync() =>
        throw new NotSupportedException("Not needed by the middleware tests.");

    public Task SaveAsync(string key, string? value)
    {
        _values[key] = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private string Resolve(string key) =>
        _values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : DefaultFor(key);

    private static string DefaultFor(string key) =>
        SettingDefinitions.All.FirstOrDefault(d => d.Key == key)?.DefaultValue
        ?? throw new KeyNotFoundException($"No setting declared for '{key}'.");
}
