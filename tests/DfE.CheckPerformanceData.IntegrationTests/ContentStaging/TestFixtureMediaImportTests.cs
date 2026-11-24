using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// The sample images the fixture pages show travel in the fixture file itself, so they arrive
// with the pages wherever the pages are imported, and before them: the import brings the images
// in first, so the pages that use them record their uses. Each start imports the file again, so
// a second import must find the images it brought last time and add nothing. Checked with the real
// upload checks, against a real database and real blob storage.
[Collection(nameof(PostgresCollection))]
public sealed class TestFixtureMediaImportTests(PostgresFixture postgres, AzuriteFixture azurite)
    : IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private static readonly Guid SamplePng = new("00000000-cd94-4a01-8f01-0000000e0201");
    private static readonly Guid SampleSvg = new("00000000-cd94-4a01-8f01-0000000e0202");
    private static readonly Guid SamplePhoto = new("00000000-cd94-4a01-8f01-0000000e0203");
    private static readonly Guid SampleAnimation = new("00000000-cd94-4a01-8f01-0000000e0204");
    private static readonly Guid ImageTestPage = new("00000000-cd94-4a01-8f01-0000000e0110");

    private static readonly string ImportFolder = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", "src", "DfE.CheckPerformanceData.Web", "Data", "Import"));

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private readonly BlobServiceClient _blobs = new(azurite.ConnectionString);
    private readonly string _container = $"media-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var ctx = postgres.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE media_usages, media_asset_versions, media_assets, media_folders, ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ContentImportResult> ImportAsync()
    {
        await using var ctx = postgres.CreateContext();
        var gate = new MediaProcessingGate(Options.Create(new MediaProcessingOptions()));
        var staging = new ContentStagingService(
            new PageNodeRepository(ctx), new ContentBlockRepository(ctx), new HomeBannerRepository(ctx, new FakeCurrentUserService()),
            new HtmlRenderingService(),
            media: new ContentMediaStaging(
                new MediaAssetRepository(ctx, new FakeCurrentUserService()), new MediaBlobStore(_blobs, _container),
                new MediaFileCleaner(new SkiaRasterImageCleaner()), gate, NullLogger<ContentMediaStaging>.Instance,
                new MediaFolderRepository(ctx, new FakeCurrentUserService())));
        var result = await new ManifestContentImporter(new PageNodeRepository(ctx), staging, NullLogger<ManifestContentImporter>.Instance)
            .ImportFileAsync(ImportFolder, TestFixtureSeedBundle.FileName);
        Assert.Empty(result.Errors);
        return result;
    }

    [Fact]
    public async Task TheImport_BringsInTheSampleImages_UnderTheirOwnIds_WithNoWarnings()
    {
        var result = await ImportAsync();

        Assert.Empty(result.Warnings);
        Assert.Equal(4, result.MediaAssetsCreated);
        await using var ctx = postgres.CreateContext();
        var types = await ctx.MediaAssets.AsNoTracking()
            .Select(a => new { a.Id, a.Versions.Single().ContentType, a.Versions.Single().IsAnimated })
            .ToDictionaryAsync(a => a.Id);
        Assert.Equal("image/png", types[SamplePng].ContentType);
        Assert.Equal("image/svg+xml", types[SampleSvg].ContentType);
        Assert.Equal("image/jpeg", types[SamplePhoto].ContentType);
        Assert.Equal("image/gif", types[SampleAnimation].ContentType);
        Assert.True(types[SampleAnimation].IsAnimated);
    }

    // The images go in before the pages, so the Image test page's version records both images.
    [Fact]
    public async Task TheImageTestPage_RecordsItsUseOfThePngAndTheSvg()
    {
        await ImportAsync();

        await using var ctx = postgres.CreateContext();
        var used = await ctx.MediaUsages.AsNoTracking()
            .Where(u => u.PageNodeId == ImageTestPage)
            .Select(u => u.AssetId).Distinct().ToListAsync();
        Assert.Equal([SamplePng, SampleSvg], used.Order());
    }

    [Fact]
    public async Task TheFixtureContentBlock_RecordsItsUseOfThePhoto()
    {
        await ImportAsync();

        await using var ctx = postgres.CreateContext();
        Assert.True(await ctx.MediaUsages.AsNoTracking()
            .AnyAsync(u => u.ContentBlockId != null && u.AssetId == SamplePhoto));
    }

    // Every start imports the file again. The second import finds each image as it left it, and
    // adds nothing.
    [Fact]
    public async Task ImportingAgain_FindsTheImagesItBroughtLastTime_AndAddsNothing()
    {
        await ImportAsync();
        int blockVersions, usages;
        await using (var before = postgres.CreateContext())
        {
            blockVersions = await before.ContentBlockVersions.CountAsync();
            usages = await before.MediaUsages.CountAsync();
        }

        var again = await ImportAsync();

        Assert.Equal(0, again.MediaAssetsCreated);
        Assert.Equal(0, again.MediaAssetsUpdated);
        Assert.Equal(4, again.MediaAssetsReused);
        await using var ctx = postgres.CreateContext();
        Assert.Equal(4, await ctx.MediaAssets.CountAsync());
        Assert.Equal(4, await ctx.MediaAssetVersions.CountAsync());
        Assert.All(await ctx.MediaAssets.Select(a => a.CurrentVersion).ToListAsync(), v => Assert.Equal(1, v));
        // The fixture block is the same as last time, so it gets no new version.
        Assert.Equal(blockVersions, await ctx.ContentBlockVersions.CountAsync());
        Assert.Equal(usages, await ctx.MediaUsages.CountAsync());
    }

    // The samples land in their sample folders, with the fixture's folder ids. Importing again
    // makes no folder and moves no image.
    [Fact]
    public async Task TheSampleImages_LandInTheirSampleFolders_Once()
    {
        var first = await ImportAsync();
        var again = await ImportAsync();

        Assert.Equal(3, first.MediaFoldersCreated);
        Assert.Equal(0, again.MediaFoldersCreated);
        await using var ctx = postgres.CreateContext();
        var folders = await ctx.MediaFolders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        var photoFolder = (await ctx.MediaAssets.SingleAsync(a => a.Id == SamplePhoto)).FolderId!.Value;
        Assert.Equal("Photos", folders[photoFolder].Name);
        Assert.Equal("Samples", folders[folders[photoFolder].ParentId!.Value].Name);
        Assert.Equal(new Guid("00000000-cd94-4a01-8f01-0000000e0303"), photoFolder);
        Assert.Null((await ctx.MediaAssets.SingleAsync(a => a.Id == SampleAnimation)).FolderId);
    }
}
