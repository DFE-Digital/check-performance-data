using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Settings;

[Collection(nameof(PostgresCollection))]
public sealed class SettingRepositoryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateSettingsAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""Settings"";";
        await cmd.ExecuteNonQueryAsync();
    }

    // Fresh context per read so assertions see committed DB state, not a tracked cache.
    private SettingRepository NewRepo() => new(_fixture.CreateContext());

    [Fact]
    public async Task Upsert_InsertsThenUpdates_GetValueReflectsLatest()
    {
        await TruncateSettingsAsync();

        await NewRepo().UpsertAsync("Wiki:PageLength", "30");
        Assert.Equal("30", await NewRepo().GetValueAsync("Wiki:PageLength"));

        await NewRepo().UpsertAsync("Wiki:PageLength", "45");
        Assert.Equal("45", await NewRepo().GetValueAsync("Wiki:PageLength"));
    }

    [Fact]
    public async Task GetValue_ReturnsNull_WhenAbsent()
    {
        await TruncateSettingsAsync();

        Assert.Null(await NewRepo().GetValueAsync("Wiki:PageLength"));
    }

    [Fact]
    public async Task Delete_RemovesStoredValue()
    {
        await TruncateSettingsAsync();
        await NewRepo().UpsertAsync("Wiki:PageLength", "99");

        await NewRepo().DeleteAsync("Wiki:PageLength");

        Assert.Null(await NewRepo().GetValueAsync("Wiki:PageLength"));
    }

    [Fact]
    public async Task GetAll_ReturnsStoredKeyValuePairs()
    {
        await TruncateSettingsAsync();
        await NewRepo().UpsertAsync("Wiki:PageLength", "25");

        var all = await NewRepo().GetAllAsync();

        Assert.True(all.ContainsKey("Wiki:PageLength"));
        Assert.Equal("25", all["Wiki:PageLength"]);
    }
}
