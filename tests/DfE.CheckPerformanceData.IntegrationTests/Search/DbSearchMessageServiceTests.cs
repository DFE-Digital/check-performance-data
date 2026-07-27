using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// Contract tests for DbSearchMessageService.CreateAsync — the write path the user-facing
// feedback form uses to persist a support message keyed by the visitor's session id.
//
// Persistence semantics the tests lock in:
//   - email is stored verbatim when a value is supplied
//   - email stays NULL when the caller passes null (the controller drops the value before
//     persist when the "Hide my email" checkbox was ticked, so this method never encrypts
//     or masks — what isn't stored can't leak)
//   - is_read defaults to false, read_by_admin_sub and read_at_utc are NULL on insert
//   - what_got is nullable and round-trips correctly
//   - CreateAsync returns the auto-assigned id so the caller can redirect to a
//     confirmation view keyed by it
[Collection(nameof(PostgresCollection))]
public sealed class DbSearchMessageServiceTests
{
    private readonly PostgresFixture _fixture;

    public DbSearchMessageServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private DbSearchMessageService CreateSut() => new(_fixture.CreateContext());

    [Fact]
    public async Task CreateAsync_WithNullEmail_PersistsRowWithEmailNullAndIsReadFalse()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var sut = CreateSut();
        var id = await sut.CreateAsync(
            sessionId: "session-hide",
            whatLookingFor: "how do i submit",
            whatGot: "one result",
            email: null,
            cancellationToken: CancellationToken.None);

        Assert.True(id > 0);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT session_id, what_looking_for, what_got, email, is_read,
                   read_by_admin_sub, read_at_utc
            FROM search_messages
            WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("session-hide", reader.GetString(0));
        Assert.Equal("how do i submit", reader.GetString(1));
        Assert.Equal("one result", reader.GetString(2));
        Assert.True(reader.IsDBNull(3), "email should be NULL when caller passes null");
        Assert.False(reader.GetBoolean(4));
        Assert.True(reader.IsDBNull(5), "read_by_admin_sub should be NULL on insert");
        Assert.True(reader.IsDBNull(6), "read_at_utc should be NULL on insert");
    }

    [Fact]
    public async Task CreateAsync_WithEmail_PersistsEmailVerbatim()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var sut = CreateSut();
        var id = await sut.CreateAsync(
            sessionId: "session-with-email",
            whatLookingFor: "cannot find a form",
            whatGot: null,
            email: "user@example.gov.uk",
            cancellationToken: CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT email FROM search_messages WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var email = await cmd.ExecuteScalarAsync();
        Assert.Equal("user@example.gov.uk", email);
    }

    [Fact]
    public async Task CreateAsync_WithNullWhatGot_PersistsRowSuccessfully()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var sut = CreateSut();
        var id = await sut.CreateAsync(
            sessionId: "session-no-what-got",
            whatLookingFor: "help me",
            whatGot: null,
            email: null,
            cancellationToken: CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT what_got FROM search_messages WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public async Task CreateAsync_StampsSubmittedAtUtcAtInsertTime()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var before = DateTime.UtcNow;
        var sut = CreateSut();
        var id = await sut.CreateAsync(
            sessionId: "session-timestamp",
            whatLookingFor: "when did this land",
            whatGot: null,
            email: null,
            cancellationToken: CancellationToken.None);
        var after = DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT submitted_at_utc FROM search_messages WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var submittedAt = (DateTime)(await cmd.ExecuteScalarAsync())!;
        var submittedAtUtc = DateTime.SpecifyKind(submittedAt, DateTimeKind.Utc);
        Assert.InRange(submittedAtUtc, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
