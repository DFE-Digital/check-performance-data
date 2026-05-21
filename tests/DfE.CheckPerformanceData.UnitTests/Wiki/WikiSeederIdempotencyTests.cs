using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

/// <summary>
/// Server-side idempotency guard on WikiSeeder.SeedAsync. Editor double-clicks past
/// the GDS data-prevent-double-click 1000ms window were the historical cause of
/// the wiki (2) / (3) duplicate-page landmine (see user memory
/// `cpd_seeding_correct_picture`). When the canonical seed root slug
/// ("getting-started") is already present, SeedAsync must exit without calling
/// IWikiService.CreatePageAsync.
/// </summary>
public sealed class WikiSeederIdempotencyTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();
    private readonly IWikiRepository _repository = Substitute.For<IWikiRepository>();
    private int _nextId = 1;

    public WikiSeederIdempotencyTests()
    {
        _wikiService.CreatePageAsync(Arg.Any<CreateWikiPageDto>())
            .Returns(ci =>
            {
                var dto = ci.Arg<CreateWikiPageDto>();
                return new WikiPageDto
                {
                    Id = _nextId++,
                    Title = dto.Title,
                    Slug = dto.Title.ToLowerInvariant().Replace(" ", "-"),
                    ParentId = dto.ParentId
                };
            });
    }

    // --- SeedAsync_ExitsEarly_WhenCanonicalRootSlug_AlreadyExists ---

    [Fact]
    public async Task SeedAsync_ExitsEarly_WhenCanonicalRootSlug_AlreadyExists()
    {
        // The "Getting started" root in the seed tree slugifies to "getting-started"
        // at the root level (ParentId == null). When the seeder finds an existing
        // page at that canonical slug, it must no-op.
        _repository.SlugExistsAsync("getting-started", null).Returns(true);

        var sut = new WikiSeeder(_wikiService, _repository);

        await sut.SeedAsync();

        await _wikiService.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    // --- SeedAsync_CreatesPages_WhenCanonicalRootSlug_Absent ---

    [Fact]
    public async Task SeedAsync_CreatesPages_WhenCanonicalRootSlug_Absent()
    {
        // Happy-path: the guard does not interfere with a clean seed.
        _repository.SlugExistsAsync("getting-started", null).Returns(false);

        var sut = new WikiSeeder(_wikiService, _repository);

        await sut.SeedAsync();

        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Getting started" && d.ParentId == null));
    }

    // --- SeedAsync_AsksRepository_ForCanonicalRootSlug_BeforeCreating ---

    [Fact]
    public async Task SeedAsync_AsksRepository_ForCanonicalRootSlug_BeforeCreating()
    {
        _repository.SlugExistsAsync("getting-started", null).Returns(false);

        var sut = new WikiSeeder(_wikiService, _repository);

        await sut.SeedAsync();

        await _repository.Received().SlugExistsAsync("getting-started", null);
    }
}
