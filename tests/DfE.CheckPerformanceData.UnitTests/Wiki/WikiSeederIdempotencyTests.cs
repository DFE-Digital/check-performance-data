using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

/// <summary>
/// Additive idempotency for WikiSeeder.SeedAsync. Re-running the seeder re-creates only
/// the sample pages that are currently missing and never produces "(2)" / "(3)" duplicate
/// titles. This replaces the prior all-or-nothing guard keyed off the "getting-started"
/// root, which turned the admin "Seed sample pages" tile into a silent no-op once any
/// content existed (and prevented re-adding pages an editor had deleted). Per-page skip is
/// delegated to IWikiService.CreatePageIfMissingAsync.
/// </summary>
public sealed class WikiSeederIdempotencyTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();
    private int _nextId = 1;

    private WikiSeeder CreateSut(ISet<string> existingTitles)
    {
        _wikiService.CreatePageIfMissingAsync(Arg.Any<CreateWikiPageDto>())
            .Returns(ci =>
            {
                var dto = ci.Arg<CreateWikiPageDto>();
                var page = new WikiPageDto
                {
                    Id = _nextId++,
                    Title = dto.Title,
                    Slug = dto.Title.ToLowerInvariant().Replace(" ", "-"),
                    ParentId = dto.ParentId
                };
                return new WikiPageCreationResult(page, Created: !existingTitles.Contains(dto.Title));
            });
        return new WikiSeeder(_wikiService);
    }

    [Fact]
    public async Task SeedAsync_ReturnsZero_WhenEveryPageAlreadyExists()
    {
        // Every page reports already-present: nothing is created, but the tree is still
        // walked so children are correctly parented under existing pages.
        var sut = CreateSut(AllTitles());

        var created = await sut.SeedAsync();

        Assert.Equal(0, created);
    }

    [Fact]
    public async Task SeedAsync_CreatesOnlyMissingPages_WhenSomeExist()
    {
        // "Getting started" and its three children already exist (4 of 21 pages present);
        // the seeder must re-create only the 17 missing pages.
        var present = new HashSet<string>
        {
            "Getting started", "Requesting an account", "Signing in", "Roles and permissions"
        };
        var sut = CreateSut(present);

        var created = await sut.SeedAsync();

        Assert.Equal(21 - 4, created);
    }

    [Fact]
    public async Task SeedAsync_NeverAppendsRunCounter_AndNeverUsesThrowingCreate()
    {
        var sut = CreateSut(new HashSet<string> { "Getting started" });

        await sut.SeedAsync();

        // No "(2)"/"(3)" titles are ever requested...
        await _wikiService.DidNotReceive().CreatePageIfMissingAsync(
            Arg.Is<CreateWikiPageDto>(d => d.Title.Contains('(')));
        // ...and the legacy throw-on-duplicate create path is no longer used by the seeder.
        await _wikiService.DidNotReceive().CreatePageAsync(Arg.Any<CreateWikiPageDto>());
    }

    private static HashSet<string> AllTitles() =>
    [
        "Getting started", "Requesting an account", "Signing in", "Roles and permissions",
        "Checking exercises", "Key Stage 2", "KS2 checking", "KS2 checked",
        "Key Stage 4", "KS4 June checking", "KS4 checking", "KS4 checked",
        "16 to 18 performance data",
        "Submitting amendments", "Reading the guidance", "Errata for late re-marks", "Tracking your request",
        "Help and support", "Frequently asked questions", "Security advice", "Contact the helpline"
    ];
}
