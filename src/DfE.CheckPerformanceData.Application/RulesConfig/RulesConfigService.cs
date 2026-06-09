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

    public Task<RulesConfigSaveResult> SaveRulesAsync(RuleSet rules, string? expectedETag, CancellationToken ct = default) =>
        SaveRulesCoreAsync(rules, expectedETag, "Save", ct);

    public Task<RulesConfigSaveResult> SaveLookupsAsync(Lookups lookups, string? expectedETag, CancellationToken ct = default) =>
        SaveLookupsCoreAsync(lookups, expectedETag, "Save", ct);

    public async Task<RulesConfigSaveResult> ImportRulesAsync(string json, string? expectedETag, CancellationToken ct = default)
    {
        RuleSet? rules;
        try
        {
            rules = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options);
        }
        catch (JsonException ex)
        {
            return RulesConfigSaveResult.Invalid(new[] { $"The file is not valid rules JSON: {ex.Message}" });
        }

        return rules is null
            ? RulesConfigSaveResult.Invalid(new[] { "The file is empty or not valid rules JSON." })
            : await SaveRulesCoreAsync(rules, expectedETag, "Import", ct);
    }

    public async Task<RulesConfigSaveResult> ImportLookupsAsync(string json, string? expectedETag, CancellationToken ct = default)
    {
        Lookups lookups;
        try
        {
            lookups = DeserialiseLookups(json);
        }
        catch (JsonException ex)
        {
            return RulesConfigSaveResult.Invalid(new[] { $"The file is not valid country-languages JSON: {ex.Message}" });
        }

        return lookups.CountryLanguages.Count == 0
            ? RulesConfigSaveResult.Invalid(new[] { "The file contains no country-language entries." })
            : await SaveLookupsCoreAsync(lookups, expectedETag, "Import", ct);
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
            return await SaveRulesCoreAsync(rules, expectedETag, "Rollback", ct);
        }

        return await SaveLookupsCoreAsync(DeserialiseLookups(snapshot.Content), expectedETag, "Rollback", ct);
    }

    private async Task<RulesConfigSaveResult> SaveRulesCoreAsync(RuleSet rules, string? expectedETag, string action, CancellationToken ct)
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
        return await PersistAsync(RulesConfigType.Rules, json, expectedETag, action, ct);
    }

    private async Task<RulesConfigSaveResult> SaveLookupsCoreAsync(Lookups lookups, string? expectedETag, string action, CancellationToken ct)
    {
        var validation = lookupsValidator.Validate(lookups);
        if (!validation.IsValid)
        {
            return RulesConfigSaveResult.Invalid(validation.Errors);
        }

        var json = SerialiseLookups(lookups);
        return await PersistAsync(RulesConfigType.Lookups, json, expectedETag, action, ct);
    }

    // Write the blob FIRST and OUTSIDE the transaction. An ETag conflict therefore aborts before
    // any DB row is written, and — crucially — the blob write is never re-run by EF's retrying
    // execution strategy (a retry would otherwise re-send a now-consumed ETag and raise a spurious
    // conflict). The version snapshot + audit then commit together inside the transaction; they are
    // safe to retry. A DB failure after a successful blob write leaves the live blob advanced without
    // a version row — acceptable because the history is append-only and self-heals on the next save.
    private async Task<RulesConfigSaveResult> PersistAsync(
        RulesConfigType type, string json, string? expectedETag, string action, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var who = string.IsNullOrWhiteSpace(currentUser.DisplayName) ? currentUser.UserId : currentUser.DisplayName;

        await store.WriteAsync(type, json, expectedETag, ct);

        var nextVersion = 0;
        await versions.ExecuteInTransactionAsync(async () =>
        {
            nextVersion = await versions.GetMaxVersionNumberAsync(type, ct) + 1;
            await versions.AddVersionAsync(type, nextVersion, json, who, now, ct);
            await versions.AddAuditAsync("RulesConfig", type.ToString(), action, currentUser.UserId, now, ct);
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
