using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// One-off feasibility spike: prove that a projection of per-field ts_rank values via
// EF.Functions.ToTsVector("english", <field>).Rank(EF.Functions.WebSearchToTsQuery(...))
// translates cleanly on the currently-pinned Npgsql provider (10.0.3) against Postgres 17.
//
// The follow-on work that widens SearchPagesAsync and ContentBlockRepository.SearchAsync
// to project per-field rank breakdowns needs this shape to translate — if it does not,
// the widened queries fall back to raw SQL. Running the spike now de-risks the widening
// before it starts. The assertion is intentionally weak: any float value >= 0 is proof
// that the LINQ tree turned into SQL and the SQL executed to completion; the rank
// magnitudes themselves are not the point.
//
// May be deleted after the widening lands — the widened repository tests will exercise
// the same translation continuously, so this test's role as "does it translate at all"
// is retired once those tests exist.
[Collection(nameof(PostgresCollection))]
public sealed class PerFieldTsRankSpikeTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ContentBlockVersions"", ""ContentBlocks"", ""PageNodeVersions"", ""PageNodes"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task EFFunctions_ToTsVectorRank_TranslatesAndReturnsNonNullFloat()
    {
        await TruncateAsync();

        var now = DateTime.UtcNow;
        var pageId = Guid.NewGuid();
        await using (var seedCtx = _fixture.CreateContext())
        {
            seedCtx.PageNodes.Add(new PageNode
            {
                Id = pageId,
                Segment = "spike",
                Path = "spike",
                Title = "Spike page",
                Subtitle = "spike subtitle",
                Keywords = "widget spike",
                PageType = "content",
                AppearInSearch = true,
                CreatedDate = now,
                UpdatedDate = now,
            });
            seedCtx.PageNodeVersions.Add(new PageNodeVersion
            {
                Id = Guid.NewGuid(),
                PageNodeId = pageId,
                VersionId = 1,
                MinorVersion = 0,
                IsCurrent = true,
                Content = "[]",
                BodyPlainText = "widget in body",
                CreatedDate = now,
                UpdatedDate = now,
            });
            await seedCtx.SaveChangesAsync();
        }

        var normalisedTerm = "widget";

        await using var queryCtx = _fixture.CreateContext();
        var row = await queryCtx.PageNodes
            .AsNoTracking()
            .Where(n => n.Id == pageId)
            // Inline EF.Functions.WebSearchToTsQuery at every call site — its `config`
            // argument is [NotParameterized], so hoisting the expression into a local
            // forces client evaluation and throws.
            .Select(n => new
            {
                RankKeywords = EF.Functions.ToTsVector("english", n.Keywords ?? string.Empty)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)),
                RankTitle = EF.Functions.ToTsVector("english", n.Title)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)),
                RankSubtitle = EF.Functions.ToTsVector("english", n.Subtitle ?? string.Empty)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)),
                RankBody = n.Versions
                    .Where(v => v.IsCurrent)
                    .Select(v => EF.Functions.ToTsVector("english", v.BodyPlainText)
                        .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)))
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync();

        Assert.NotNull(row);
        Assert.True(row!.RankKeywords >= 0f, $"RankKeywords: {row.RankKeywords}");
        Assert.True(row.RankTitle >= 0f, $"RankTitle: {row.RankTitle}");
        Assert.True(row.RankSubtitle >= 0f, $"RankSubtitle: {row.RankSubtitle}");
        Assert.True(row.RankBody >= 0f, $"RankBody: {row.RankBody}");
    }
}
