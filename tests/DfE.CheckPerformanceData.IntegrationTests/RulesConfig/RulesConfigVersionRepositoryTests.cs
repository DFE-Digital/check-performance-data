using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesConfig;

[Collection(nameof(PostgresCollection))]
public sealed class RulesConfigVersionRepositoryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private RulesConfigVersionRepository NewRepo() => new(_fixture.CreateContext());

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""RulesConfigVersions"" RESTART IDENTITY; TRUNCATE ""AuditEntries"" RESTART IDENTITY;";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Add_then_list_returns_newest_first()
    {
        await TruncateAsync();
        await NewRepo().AddVersionAsync(RulesConfigType.Rules, 1, "{\"v\":1}", "alice", DateTime.UtcNow.AddMinutes(-2));
        await NewRepo().AddVersionAsync(RulesConfigType.Rules, 2, "{\"v\":2}", "bob", DateTime.UtcNow);

        var list = await NewRepo().ListAsync(RulesConfigType.Rules);

        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[0].VersionNumber);
        Assert.Equal("bob", list[0].CreatedBy);
    }

    [Fact]
    public async Task GetMaxVersionNumber_is_zero_when_empty_then_tracks_max()
    {
        await TruncateAsync();
        Assert.Equal(0, await NewRepo().GetMaxVersionNumberAsync(RulesConfigType.Lookups));
        await NewRepo().AddVersionAsync(RulesConfigType.Lookups, 1, "{}", "alice", DateTime.UtcNow);
        Assert.Equal(1, await NewRepo().GetMaxVersionNumberAsync(RulesConfigType.Lookups));
    }

    [Fact]
    public async Task Versions_are_isolated_by_config_type()
    {
        await TruncateAsync();
        await NewRepo().AddVersionAsync(RulesConfigType.Rules, 1, "{\"rules\":true}", "a", DateTime.UtcNow);
        var lookups = await NewRepo().ListAsync(RulesConfigType.Lookups);
        Assert.Empty(lookups);
    }

    [Fact]
    public async Task AddAudit_writes_a_row()
    {
        await TruncateAsync();
        await NewRepo().AddAuditAsync("RulesConfig", "Rules", "Save", "alice", DateTime.UtcNow);
        await using var ctx = _fixture.CreateContext();
        Assert.Equal(1, ctx.AuditEntries.Count(a => a.EntityType == "RulesConfig" && a.Action == "Save"));
    }

    [Fact]
    public async Task GetById_returns_dto_when_present_and_null_when_absent()
    {
        await TruncateAsync();
        Assert.Null(await NewRepo().GetByIdAsync(999));

        await NewRepo().AddVersionAsync(RulesConfigType.Rules, 1, "{\"v\":1}", "alice", DateTime.UtcNow);
        var saved = (await NewRepo().ListAsync(RulesConfigType.Rules))[0];

        var fetched = await NewRepo().GetByIdAsync(saved.Id);
        Assert.NotNull(fetched);
        Assert.Equal(1, fetched!.VersionNumber);
        Assert.Equal("alice", fetched.CreatedBy);
    }
}
