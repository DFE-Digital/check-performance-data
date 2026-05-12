using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

public sealed class WikiSeederTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();
    private readonly WikiSeeder _sut;
    private int _nextId = 1;

    public WikiSeederTests()
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

        _sut = new WikiSeeder(_wikiService);
    }

    [Fact]
    public async Task SeedAsync_CreatesFourTopLevelSections()
    {
        await _sut.SeedAsync();

        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Getting started" && d.ParentId == null));
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Checking exercises" && d.ParentId == null));
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Submitting amendments" && d.ParentId == null));
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Help and support" && d.ParentId == null));
    }

    [Fact]
    public async Task SeedAsync_CreatesChildrenUnderTheirCorrectParents()
    {
        await _sut.SeedAsync();

        // "Getting started" was the first page created, so its returned ID is 1.
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Requesting an account" && d.ParentId == 1));
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Signing in" && d.ParentId == 1));
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Roles and permissions" && d.ParentId == 1));
    }

    [Fact]
    public async Task SeedAsync_CreatesGrandchildrenUnderKeyStageParents()
    {
        await _sut.SeedAsync();

        // The Key Stage children must have a non-null parent that is itself parented (a grandchild).
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "KS2 checking" && d.ParentId != null));
        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "KS4 June checking" && d.ParentId != null));
    }

    [Fact]
    public async Task SeedAsync_WhenRootSlugAlreadyExists_AppendsRunCounterAndRetries()
    {
        // First call to "Getting started" throws; second call (with retry) succeeds.
        _wikiService.CreatePageAsync(
                Arg.Is<CreateWikiPageDto>(d => d.Title == "Getting started" && d.ParentId == null))
            .Returns<WikiPageDto>(_ => throw new DuplicateWikiPageException("Getting started", null));

        await _sut.SeedAsync();

        await _wikiService.Received().CreatePageAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title == "Getting started (2)" && d.ParentId == null));
    }
}
