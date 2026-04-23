using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Wiki;

[Collection(nameof(PostgresCollection))]
public sealed class WikiRepositorySearchAsyncTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    // --- Schema ---

    [Fact]
    public async Task Migration_CreatesGinIndexOnSearchVector()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT indexdef FROM pg_indexes
                            WHERE tablename = 'WikiPages'
                              AND indexname = 'IX_WikiPages_SearchVector';";
        var indexdef = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(indexdef);
        Assert.Contains("USING gin", indexdef);
        Assert.Contains("SearchVector", indexdef);
    }

    [Fact]
    public async Task Migration_CreatesStoredGeneratedSearchVectorColumn()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Cast attgenerated (Postgres "char" type, 1 byte) to text so Npgsql returns a string.
        cmd.CommandText = @"SELECT attgenerated::text FROM pg_attribute
                            WHERE attrelid = '""WikiPages""'::regclass
                              AND attname = 'SearchVector';";
        var attgenerated = (string?)await cmd.ExecuteScalarAsync();

        // 's' = stored generated column
        Assert.Equal("s", attgenerated);
    }

    // --- Matches ---

    [Fact]
    public async Task Matches_ReturnsTitleSlugAndSnippet()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 04): seed pages + assert title/slug/snippet round-trip
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Snippet_WrapsMatchesWithMark()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 04): assert snippet contains <mark>...</mark>
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TitleMatch_OutranksBody()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 04): assert setweight A > B ordering
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Stemming_MatchesInflectedForms()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 04): assert "publishing" matches page "publish"
        await Task.CompletedTask;
    }

    // --- Access control ---

    [Fact]
    public async Task ExcludesSoftDeleted()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 04): assert soft-deleted page absent, no IgnoreQueryFilters used
        await Task.CompletedTask;
    }

    // --- Query robustness ---

    [Fact]
    public async Task HandlesUnbalancedQuotes()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 04): assert query "hello \"world" does not throw
        await Task.CompletedTask;
    }

    // --- Write-path drift ---

    [Fact]
    public async Task UpdatePage_RefreshesBodyPlainTextAndSearchVector()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 05): assert update re-indexes
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RevertToVersion_RefreshesBodyPlainText()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 05): assert revert re-indexes
        await Task.CompletedTask;
    }
}
