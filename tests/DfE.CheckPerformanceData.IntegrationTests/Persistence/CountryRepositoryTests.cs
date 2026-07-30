using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

/// <summary>
/// Pins <see cref="CountryRepository.GetCodeByNameAsync"/> to an exact, case-insensitive
/// name match. The name it receives is a posted form value (the autocomplete's hidden label
/// field), and the resolved code is both stored on the journey and backfilled onto the answer
/// the rules engine reads — so a pattern metacharacter must never widen the match.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CountryRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly Guid FranceId = Guid.Parse("cc000000-0000-0000-0000-000000000001");
    private static readonly Guid FinlandId = Guid.Parse("cc000000-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await using var ctx = fixture.CreateContext();
        ctx.Countries.AddRange(
            new Country { Id = FranceId, Code = "FR", Name = "France", OfficialName = "French Republic", Kind = CountryKind.Sovereign },
            new Country { Id = FinlandId, Code = "FI", Name = "Finland", OfficialName = "Republic of Finland", Kind = CountryKind.Sovereign });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Countries.Where(c => c.Id == FranceId || c.Id == FinlandId).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData("France")]
    [InlineData("france")]
    [InlineData("FRANCE")]
    public async Task GetCodeByNameAsync_MatchesExactNameIgnoringCase(string name)
    {
        await using var ctx = fixture.CreateContext();
        var sut = new CountryRepository(ctx);

        Assert.Equal("FR", await sut.GetCodeByNameAsync(name));
    }

    [Theory]
    [InlineData("%")]           // would match every row if treated as a LIKE pattern
    [InlineData("F%")]
    [InlineData("Franc_")]
    [InlineData("_rance")]
    public async Task GetCodeByNameAsync_TreatsPatternMetacharactersAsLiterals(string name)
    {
        await using var ctx = fixture.CreateContext();
        var sut = new CountryRepository(ctx);

        Assert.Null(await sut.GetCodeByNameAsync(name));
    }

    [Fact]
    public async Task GetCodeByNameAsync_UnknownName_ReturnsNull()
    {
        await using var ctx = fixture.CreateContext();
        var sut = new CountryRepository(ctx);

        Assert.Null(await sut.GetCodeByNameAsync("Atlantis"));
    }
}
