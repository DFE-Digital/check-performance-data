using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SkiaSharp;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// The upload pipeline end to end: real Skia cleaning, real blob storage (Azurite) and a real
// database. Proves what is stored in each place for a new image, a duplicate, a replace and a
// refused file, and that the audit log records uploads and replaces.
[Collection(nameof(PostgresCollection))]
public sealed class MediaUploadServiceTests(PostgresFixture postgres, AzuriteFixture azurite)
    : IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private readonly BlobServiceClient _blobs = new(azurite.ConnectionString);
    private readonly string _container = $"media-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var ctx = postgres.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE media_usages, media_asset_versions, media_assets");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MediaBlobStore BlobStore => new(_blobs, _container);

    private MediaUploadService Service(PortalDbContext ctx, IMediaAssetRepository? repository = null) => new(
        new MediaFileCleaner(new SkiaRasterImageCleaner()),
        repository ?? new MediaAssetRepository(ctx, new FakeCurrentUserService()),
        BlobStore,
        new MediaProcessingGate(Options.Create(new MediaProcessingOptions())),
        NullLogger<MediaUploadService>.Instance,
        new MediaFolderRepository(ctx, new FakeCurrentUserService()));

    private static byte[] Png(int width, int height, SKColor colour)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }

    private async Task<byte[]> StoredBytesAsync(string key)
    {
        await using var blob = await BlobStore.GetAsync(key);
        Assert.NotNull(blob);
        using var copy = new MemoryStream();
        await blob!.Content.CopyToAsync(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task Upload_StoresTheCleanedFileAndItsRecord()
    {
        await using var ctx = postgres.CreateContext();

        var result = await Service(ctx).UploadAsync(new MemoryStream(Png(30, 20, SKColors.Red)), "Playground.png");

        Assert.Equal(MediaUploadOutcome.Created, result.Outcome);
        var asset = result.Asset!;
        await using var read = postgres.CreateContext();
        var saved = await new MediaAssetRepository(read, new FakeCurrentUserService()).GetAsync(asset.Id);
        Assert.NotNull(saved);
        Assert.Equal("Playground.png", saved!.FileName);
        Assert.Equal("playground", saved.Slug);
        Assert.Equal("test-user@education.gov.uk", saved.CreatedBy);
        Assert.Equal(MediaBlobKeys.Original(asset.Id, 1), saved.Latest.BlobKey);
        Assert.Equal("image/png", saved.Latest.ContentType);
        Assert.Equal((30, 20), (saved.Latest.Width, saved.Latest.Height));

        var stored = await StoredBytesAsync(saved.Latest.BlobKey);
        Assert.Equal(stored.Length, saved.Latest.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(stored)), saved.Latest.Sha256);
        await using (var blob = await BlobStore.GetAsync(saved.Latest.BlobKey))
            Assert.Equal("image/png", blob!.ContentType);
    }

    [Fact]
    public async Task UploadingTheSameFileAgain_ReturnsTheExistingImage_AndStoresNothingNew()
    {
        var png = Png(12, 12, SKColors.Blue);
        Guid firstId;
        await using (var ctx = postgres.CreateContext())
            firstId = (await Service(ctx).UploadAsync(new MemoryStream(png), "first.png")).Asset!.Id;

        await using var again = postgres.CreateContext();
        var result = await Service(again).UploadAsync(new MemoryStream(png), "second.png");

        Assert.Equal(MediaUploadOutcome.Duplicate, result.Outcome);
        Assert.Equal(firstId, result.Asset!.Id);
        await using var read = postgres.CreateContext();
        Assert.Equal(1, await read.MediaAssets.CountAsync());
        var blobs = new List<string>();
        await foreach (var item in _blobs.GetBlobContainerClient(_container).GetBlobsAsync())
            blobs.Add(item.Name);
        Assert.Equal([MediaBlobKeys.Original(firstId, 1)], blobs);
    }

    [Fact]
    public async Task Replace_AddsTheNextVersion_AndKeepsTheOldFile()
    {
        Guid id;
        await using (var ctx = postgres.CreateContext())
            id = (await Service(ctx).UploadAsync(new MemoryStream(Png(10, 10, SKColors.Red)), "logo.png")).Asset!.Id;

        await using (var ctx = postgres.CreateContext())
        {
            var result = await Service(ctx).ReplaceAsync(id, new MemoryStream(Png(40, 10, SKColors.Green)));
            Assert.Equal(MediaReplaceOutcome.Replaced, result.Outcome);
            Assert.Equal(2, result.Version!.Version);
        }

        await using var read = postgres.CreateContext();
        var repository = new MediaAssetRepository(read, new FakeCurrentUserService());
        var asset = await repository.GetAsync(id);
        Assert.Equal(2, asset!.CurrentVersion);
        Assert.Equal((40, 10), (asset.Latest.Width, asset.Latest.Height));
        Assert.Equal("logo.png", asset.FileName);
        var versions = await repository.GetVersionsAsync(id);
        Assert.Equal([2, 1], versions.Select(v => v.Version));
        Assert.True(await BlobStore.ExistsAsync(MediaBlobKeys.Original(id, 1)));
        Assert.True(await BlobStore.ExistsAsync(MediaBlobKeys.Original(id, 2)));
    }

    [Fact]
    public async Task RefusedFiles_LeaveNoRowAndNoBlob()
    {
        await using var ctx = postgres.CreateContext();
        var sut = Service(ctx);

        var html = await sut.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")), "cat.png");
        var polyglot = await sut.UploadAsync(new MemoryStream(Encoding.ASCII.GetBytes("GIF89a=1;alert(1)//")), "cat.gif");

        Assert.Equal(MediaUploadOutcome.Rejected, html.Outcome);
        Assert.Equal(MediaRejections.SvgUnreadable, html.Rejection);
        Assert.Equal(MediaUploadOutcome.Rejected, polyglot.Outcome);
        Assert.Equal(MediaRejections.Unreadable, polyglot.Rejection);
        await using var read = postgres.CreateContext();
        Assert.False(await read.MediaAssets.AnyAsync());
        Assert.False(await _blobs.GetBlobContainerClient(_container).ExistsAsync());
    }

    [Fact]
    public async Task Replace_WithARefusedFile_KeepsTheCurrentVersion()
    {
        Guid id;
        await using (var ctx = postgres.CreateContext())
            id = (await Service(ctx).UploadAsync(new MemoryStream(Png(10, 10, SKColors.Red)), "logo.png")).Asset!.Id;

        await using var replace = postgres.CreateContext();
        var result = await Service(replace).ReplaceAsync(id, new MemoryStream("%PDF-1.7"u8.ToArray()));

        Assert.Equal(MediaReplaceResult.Rejected(MediaRejections.NotAnImage), result);
        await using var read = postgres.CreateContext();
        Assert.Equal(1, (await read.MediaAssets.SingleAsync(a => a.Id == id)).CurrentVersion);
        Assert.False(await BlobStore.ExistsAsync(MediaBlobKeys.Original(id, 2)));
    }

    [Fact]
    public async Task Upload_WhoseRowFailsToSave_RemovesTheStoredFile()
    {
        var failing = Substitute.For<IMediaAssetRepository>();
        failing.FindBySha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        failing.CreateAsync(Arg.Any<NewMediaAsset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("database refused"));
        await using var ctx = postgres.CreateContext();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Service(ctx, failing).UploadAsync(new MemoryStream(Png(8, 8, SKColors.Red)), "a.png"));

        var key = failing.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<NewMediaAsset>().Single().FirstVersion.BlobKey;
        Assert.False(await BlobStore.ExistsAsync(key));
    }

    [Fact]
    public async Task UploadAndReplace_AreRecordedInTheAuditLog()
    {
        Guid id;
        await using (var ctx = postgres.CreateContext())
            id = (await Service(ctx).UploadAsync(new MemoryStream(Png(10, 10, SKColors.Red)), "audit.png")).Asset!.Id;
        await using (var ctx = postgres.CreateContext())
            await Service(ctx).ReplaceAsync(id, new MemoryStream(Png(10, 10, SKColors.Blue)));

        await using var read = postgres.CreateContext();
        var idText = id.ToString();
        var rows = await read.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId != null && a.EntityId.StartsWith(idText))
            .ToListAsync();
        Assert.Contains(rows, r => r.EntityType == nameof(MediaAsset) && r.Action == "Insert");
        Assert.Contains(rows, r => r.EntityType == nameof(MediaAssetVersion) && r.Action == "Insert" && r.EntityId == $"{id},1");
        Assert.Contains(rows, r => r.EntityType == nameof(MediaAssetVersion) && r.Action == "Insert" && r.EntityId == $"{id},2");
        Assert.Contains(rows, r => r.EntityType == nameof(MediaAsset) && r.Action == "Update");
        Assert.All(rows, r => Assert.Equal("test-user", r.UserId));
    }
}
