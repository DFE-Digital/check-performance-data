using System.Text.Json;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesConfig;

public sealed class RulesConfigServiceTests
{
    private sealed class FakeStore : IRulesConfigStore
    {
        public Dictionary<RulesConfigType, string> Content = new();
        public string CurrentETag = "etag-0";
        public int Writes;
        public string? LastExpectedETag = "UNSET";

        public Task<RulesConfigBlob> ReadAsync(RulesConfigType type, CancellationToken ct = default) =>
            Task.FromResult(new RulesConfigBlob(Content.GetValueOrDefault(type, ""), CurrentETag));

        public Task WriteAsync(RulesConfigType type, string content, string? expectedETag, CancellationToken ct = default)
        {
            LastExpectedETag = expectedETag;
            Content[type] = content;
            Writes++;
            CurrentETag = $"etag-{Writes}";
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepo : IRulesConfigVersionRepository
    {
        public List<RulesConfigVersionDto> Versions = new();
        public List<string> Audits = new();
        private int _nextId = 1;

        public Task<int> GetMaxVersionNumberAsync(RulesConfigType type, CancellationToken ct = default) =>
            Task.FromResult(Versions.Where(v => v.ConfigType == type).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max());

        public Task AddVersionAsync(RulesConfigType type, int versionNumber, string content, string? createdBy,
            DateTime createdAt, CancellationToken ct = default)
        {
            Versions.Add(new RulesConfigVersionDto
            {
                Id = _nextId++, ConfigType = type, VersionNumber = versionNumber,
                Content = content, CreatedBy = createdBy, CreatedAt = createdAt
            });
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RulesConfigVersionDto>> ListAsync(RulesConfigType type, CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<RulesConfigVersionDto>)Versions
                .Where(v => v.ConfigType == type).OrderByDescending(v => v.VersionNumber).ToList());

        public Task<RulesConfigVersionDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Versions.FirstOrDefault(v => v.Id == id));

        public Task AddAuditAsync(string entityType, string entityId, string action, string? userId,
            DateTime timestamp, CancellationToken ct = default)
        {
            Audits.Add($"{entityType}:{entityId}:{action}:{userId}");
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default) => work();
    }

    private sealed class FakeUser : ICurrentUserService
    {
        public string UserId => "user-1";
        public string DisplayName => "Alice Admin";
        public string Email => "alice@education.gov.uk";
        public string OrganisationId => "org";
        public string OrganisationName => "Org";
        public string OrganisationUrn => "1";
        public string OrganisationLaestab => "1";
        public string OrganisationTypeId => "1";
    }

    private static RuleSet ValidRules() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("Deceased", "Deceased",
            new[] { new RuleBranch("DEC-1", DecisionStatus.AutoApproved, Predicate.Otherwise.Instance) })
    });

    private static RuleSet InvalidRules() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("Bad", "Bad",
            new[] { new RuleBranch("B1", DecisionStatus.AutoApproved, new Predicate.FieldEq("checkingWindowType", new FieldValue.Str("KS4June"))) })
    });

    private static RulesConfigService NewService(FakeStore store, FakeRepo repo) =>
        new(store, repo, new RuleSetValidator(), new LookupsValidator(), new FakeUser(), TimeProvider.System);

    [Fact]
    public async Task SaveRules_valid_writes_blob_versions_and_audits()
    {
        var store = new FakeStore { CurrentETag = "etag-A" };
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.SaveRulesAsync(ValidRules(), expectedETag: "etag-A");

        Assert.True(result.Saved);
        Assert.Equal(1, result.VersionNumber);
        Assert.Equal(1, store.Writes);
        Assert.Equal("etag-A", store.LastExpectedETag);
        Assert.Single(repo.Versions);
        Assert.Equal("Alice Admin", repo.Versions[0].CreatedBy);
        Assert.Single(repo.Audits);

        var written = JsonSerializer.Deserialize<RuleSet>(store.Content[RulesConfigType.Rules], RulesJson.Options)!;
        Assert.Equal("Deceased", written.Outcomes[0].Key);
    }

    [Fact]
    public async Task SaveRules_invalid_writes_nothing_and_returns_errors()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.SaveRulesAsync(InvalidRules(), expectedETag: "etag-0");

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, store.Writes);
        Assert.Empty(repo.Versions);
        Assert.Empty(repo.Audits);
    }

    [Fact]
    public async Task SaveRules_increments_version_number()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        await svc.SaveRulesAsync(ValidRules(), null);
        var second = await svc.SaveRulesAsync(ValidRules(), store.CurrentETag);

        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(new[] { 1, 2 }, repo.Versions.Select(v => v.VersionNumber).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task Rollback_revalidates_and_writes_a_new_version()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        await svc.SaveRulesAsync(ValidRules(), null);
        var v1 = Assert.Single(repo.Versions);

        var result = await svc.RollbackAsync(v1.Id, store.CurrentETag);

        Assert.True(result.Saved);
        Assert.Equal(2, result.VersionNumber);
        Assert.Equal(2, repo.Versions.Count);
    }

    [Fact]
    public async Task Rollback_writes_a_rollback_audit_entry()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        await svc.SaveRulesAsync(ValidRules(), null);   // audit ":Save:"
        var v1 = repo.Versions[0];

        await svc.RollbackAsync(v1.Id, store.CurrentETag);

        Assert.Contains(repo.Audits, a => a.Contains(":Rollback:"));
        Assert.Contains(repo.Audits, a => a.Contains(":Save:"));
    }

    [Fact]
    public async Task SaveLookups_valid_writes_blob_version_and_audit()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var lookups = new Lookups(new Dictionary<string, IReadOnlyList<string>> { ["GB"] = new[] { "English" } });

        var result = await svc.SaveLookupsAsync(lookups, null);

        Assert.True(result.Saved);
        Assert.Equal(1, store.Writes);
        Assert.Single(repo.Versions);
        Assert.Equal(RulesConfigType.Lookups, repo.Versions[0].ConfigType);
        Assert.Single(repo.Audits);
    }

    [Fact]
    public async Task ImportRules_valid_json_writes_version_and_import_audit()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);
        var json = JsonSerializer.Serialize(ValidRules(), RulesJson.Options);

        var result = await svc.ImportRulesAsync(json, expectedETag: null);

        Assert.True(result.Saved);
        Assert.Equal(1, result.VersionNumber);
        Assert.Equal(1, store.Writes);
        Assert.Single(repo.Versions);
        Assert.Contains(repo.Audits, a => a.Contains(":Import:"));

        var written = JsonSerializer.Deserialize<RuleSet>(store.Content[RulesConfigType.Rules], RulesJson.Options)!;
        Assert.Equal("Deceased", written.Outcomes[0].Key);
    }

    [Fact]
    public async Task ImportRules_malformed_json_returns_error_and_writes_nothing()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.ImportRulesAsync("{ not valid json", expectedETag: null);

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, store.Writes);
        Assert.Empty(repo.Versions);
    }

    [Fact]
    public async Task ImportRules_zero_outcomes_returns_validation_error()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);
        var json = JsonSerializer.Serialize(new RuleSet("v1", DateTimeOffset.UnixEpoch, Array.Empty<OutcomeRules>()), RulesJson.Options);

        var result = await svc.ImportRulesAsync(json, expectedETag: null);

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task ImportLookups_valid_json_writes_version_and_import_audit()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.ImportLookupsAsync("{\"GB\":[\"English\",\"Welsh\"]}", expectedETag: null);

        Assert.True(result.Saved);
        Assert.Equal(1, store.Writes);
        Assert.Single(repo.Versions);
        Assert.Equal(RulesConfigType.Lookups, repo.Versions[0].ConfigType);
        Assert.Contains(repo.Audits, a => a.Contains(":Import:"));
    }

    [Fact]
    public async Task ImportLookups_empty_object_returns_error_and_writes_nothing()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.ImportLookupsAsync("{}", expectedETag: null);

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, store.Writes);
        Assert.Empty(repo.Versions);
    }

    [Fact]
    public async Task ImportLookups_malformed_json_returns_error()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.ImportLookupsAsync("not json", expectedETag: null);

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, store.Writes);
    }
}
