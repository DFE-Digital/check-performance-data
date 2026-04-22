using DfE.CheckPerformanceData.IntegrationTests.Fixtures;

namespace DfE.CheckPerformanceData.IntegrationTests.Wiki;

[Collection(nameof(PostgresCollection))]
public sealed class WikiRepositorySearchAsyncTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    // --- Schema ---

    [Fact]
    public async Task Migration_CreatesGinIndexOnSearchVector()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 03): SELECT indexdef FROM pg_indexes ... assert USING gin
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Migration_CreatesStoredGeneratedSearchVectorColumn()
    {
        await _fixture.ResetAsync();
        // TODO(Plan 03): SELECT attgenerated FROM pg_attribute ... assert 's'
        await Task.CompletedTask;
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
