using System.Text.Json;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.RulesConfig;

public sealed class RulesConfigService(
    IRulesConfigStore store,
    IRulesConfigVersionRepository versions,
    RuleSetValidator rulesValidator,
    LookupsValidator lookupsValidator,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IRulesConfigService
{
    public async Task<(RuleSet Rules, string? ETag)> GetRulesAsync(CancellationToken ct = default)
    {
        var blob = await store.ReadAsync(RulesConfigType.Rules, ct);
        var rules = JsonSerializer.Deserialize<RuleSet>(blob.Content, RulesJson.Options)
            ?? throw new InvalidOperationException("rules.json deserialised to null.");
        return (rules, blob.ETag);
    }

    public async Task<(Lookups Lookups, string? ETag)> GetLookupsAsync(CancellationToken ct = default)
    {
        var blob = await store.ReadAsync(RulesConfigType.Lookups, ct);
        return (DeserialiseLookups(blob.Content), blob.ETag);
    }

    public async Task<RulesConfigSaveResult> SaveRulesAsync(RuleSet rules, string? expectedETag, CancellationToken ct = default)
    {
        var validation = rulesValidator.Validate(rules);
        if (!validation.IsValid)
        {
            return RulesConfigSaveResult.Invalid(validation.Errors);
        }

        var stamped = validation.ResolvedRules! with
        {
            Version = NewVersionString(),
            UpdatedAt = timeProvider.GetUtcNow()
        };
        var json = JsonSerializer.Serialize(stamped, RulesJson.Options);
        return await PersistAsync(RulesConfigType.Rules, json, expectedETag, ct);
    }

    public async Task<RulesConfigSaveResult> SaveLookupsAsync(Lookups lookups, string? expectedETag, CancellationToken ct = default)
    {
        var validation = lookupsValidator.Validate(lookups);
        if (!validation.IsValid)
        {
            return RulesConfigSaveResult.Invalid(validation.Errors);
        }

        var json = SerialiseLookups(lookups);
        return await PersistAsync(RulesConfigType.Lookups, json, expectedETag, ct);
    }

    public Task<IReadOnlyList<RulesConfigVersionDto>> ListVersionsAsync(RulesConfigType type, CancellationToken ct = default) =>
        versions.ListAsync(type, ct);

    public async Task<RulesConfigSaveResult> RollbackAsync(int versionId, string? expectedETag, CancellationToken ct = default)
    {
        var snapshot = await versions.GetByIdAsync(versionId, ct)
            ?? throw new InvalidOperationException($"Rules config version {versionId} not found.");

        if (snapshot.ConfigType == RulesConfigType.Rules)
        {
            var rules = JsonSerializer.Deserialize<RuleSet>(snapshot.Content, RulesJson.Options)
                ?? throw new InvalidOperationException("Stored rules snapshot deserialised to null.");
            return await SaveRulesAsync(rules, expectedETag, ct);
        }

        return await SaveLookupsAsync(DeserialiseLookups(snapshot.Content), expectedETag, ct);
    }

    private async Task<RulesConfigSaveResult> PersistAsync(RulesConfigType type, string json, string? expectedETag, CancellationToken ct)
    {
        var nextVersion = await versions.GetMaxVersionNumberAsync(type, ct) + 1;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var who = string.IsNullOrWhiteSpace(currentUser.DisplayName) ? currentUser.UserId : currentUser.DisplayName;

        await versions.ExecuteInTransactionAsync(async () =>
        {
            await store.WriteAsync(type, json, expectedETag, ct);
            await versions.AddVersionAsync(type, nextVersion, json, who, now, ct);
            await versions.AddAuditAsync("RulesConfig", type.ToString(), "Save", currentUser.UserId, now, ct);
        }, ct);

        return RulesConfigSaveResult.Success(nextVersion);
    }

    private string NewVersionString() => timeProvider.GetUtcNow().ToString("yyyy.MM.dd-HHmmss");

    private static Lookups DeserialiseLookups(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return Lookups.Empty;
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(content, RulesJson.Options);
        return raw is null
            ? Lookups.Empty
            : new Lookups(raw.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value.ToArray()));
    }

    private static string SerialiseLookups(Lookups lookups)
    {
        var raw = lookups.CountryLanguages.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
        return JsonSerializer.Serialize(raw, RulesJson.Options);
    }
}
