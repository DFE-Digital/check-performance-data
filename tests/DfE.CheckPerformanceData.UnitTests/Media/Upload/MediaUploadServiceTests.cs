using System.Security.Cryptography;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Upload;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Upload;

// The order of an upload: clean, look for a duplicate, store the file, then record it. A record
// that fails to save takes its stored file with it, so no file is left that nothing points at.
public sealed class MediaUploadServiceTests
{
    private static readonly byte[] CleanBytes = [1, 2, 3, 4];
    private static readonly string CleanSha = Convert.ToHexStringLower(SHA256.HashData(CleanBytes));

    private readonly IMediaFileCleaner _cleaner = Substitute.For<IMediaFileCleaner>();
    private readonly IMediaAssetRepository _assets = Substitute.For<IMediaAssetRepository>();
    private readonly IMediaBlobStore _blobs = Substitute.For<IMediaBlobStore>();
    private readonly MediaProcessingGate _gate = new(Options.Create(new MediaProcessingOptions { MaxConcurrentProcessing = 2 }));
    private readonly MediaUploadService _sut;
    private readonly List<string> _calls = [];

    public MediaUploadServiceTests()
    {
        _sut = new MediaUploadService(_cleaner, _assets, _blobs, _gate, NullLogger<MediaUploadService>.Instance);
        _cleaner.CleanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(MediaCheckResult.Accept(new CleanedMediaFile(CleanBytes, MediaFormat.Png, 30, 20, false)));
        _assets.FindBySha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _blobs.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true).AndDoes(_ => _calls.Add("blob:put"));
        _assets.CreateAsync(Arg.Any<NewMediaAsset>(), Arg.Any<CancellationToken>())
            .Returns(ci => Dto(ci.Arg<NewMediaAsset>().Id)).AndDoes(_ => _calls.Add("row:create"));
    }

    private static MediaAssetVersionDto VersionDto(Guid id, int version = 1) =>
        new(id, version, MediaBlobKeys.Original(id, version), "image/png", 30, 20, 4, CleanSha, false, DateTime.UtcNow, "u");

    private static MediaAssetDto Dto(Guid id, int currentVersion = 1) =>
        new(id, "a.png", "a", null, null, null, [], currentVersion, VersionDto(id, currentVersion), DateTime.UtcNow, "u", DateTime.UtcNow, "u");

    [Fact]
    public async Task Upload_StoresTheCleanedFileThenRecordsIt()
    {
        byte[]? stored = null;
        _ = _blobs.PutAsync(Arg.Any<string>(), Arg.Do<Stream>(s => stored = ((MemoryStream)s).ToArray()), "image/png", Arg.Any<CancellationToken>());

        var result = await _sut.UploadAsync(new MemoryStream([9, 9]), "School photo.PNG");

        Assert.Equal(MediaUploadOutcome.Created, result.Outcome);
        Assert.Equal(["blob:put", "row:create"], _calls);
        Assert.Equal(CleanBytes, stored);
        await _assets.Received(1).CreateAsync(Arg.Is<NewMediaAsset>(a =>
            a.FileName == "School photo.PNG"
            && a.Slug == "school-photo"
            && a.FirstVersion.BlobKey == MediaBlobKeys.Original(a.Id, 1)
            && a.FirstVersion.ContentType == "image/png"
            && a.FirstVersion.Width == 30
            && a.FirstVersion.Height == 20
            && a.FirstVersion.SizeBytes == CleanBytes.Length
            && a.FirstVersion.Sha256 == CleanSha
            && !a.FirstVersion.IsAnimated), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(@"C:\Users\someone\Pictures\Logo.svg", "Logo.svg", "logo")]
    [InlineData("../../etc/passwd.png", "passwd.png", "passwd")]
    [InlineData("照片.png", "照片.png", "image")]
    [InlineData("   ", "image", "image")]
    public async Task Upload_KeepsOnlyTheFileNamePart_AndAlwaysHasASlug(string given, string fileName, string slug)
    {
        await _sut.UploadAsync(new MemoryStream([1]), given);

        await _assets.Received(1).CreateAsync(
            Arg.Is<NewMediaAsset>(a => a.FileName == fileName && a.Slug == slug), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_TrimsLongNamesToWhatTheRecordHolds()
    {
        await _sut.UploadAsync(new MemoryStream([1]), new string('a', 400) + ".png");

        await _assets.Received(1).CreateAsync(
            Arg.Is<NewMediaAsset>(a => a.FileName.Length == 255 && a.FileName.EndsWith(".png") && a.Slug.Length <= 200),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_OfAFileTheLibraryAlreadyHolds_ReturnsTheExistingImage_AndStoresNothing()
    {
        var existing = Dto(Guid.NewGuid());
        _assets.FindBySha256Async(CleanSha, Arg.Any<CancellationToken>()).Returns([existing]);

        var result = await _sut.UploadAsync(new MemoryStream([1]), "a.png");

        Assert.Equal(MediaUploadOutcome.Duplicate, result.Outcome);
        Assert.Same(existing, result.Asset);
        Assert.Empty(_calls);
    }

    // An image that held this file once but has since been replaced shows something else now,
    // so offering it as "the same image" would be wrong.
    [Fact]
    public async Task Upload_OfAFileOnlyAnOlderVersionHeld_IsAddedAsANewImage()
    {
        var replaced = Dto(Guid.NewGuid(), currentVersion: 2) with
        {
            Latest = VersionDto(Guid.NewGuid(), 2) with { Sha256 = new string('f', 64) }
        };
        _assets.FindBySha256Async(CleanSha, Arg.Any<CancellationToken>()).Returns([replaced]);

        var result = await _sut.UploadAsync(new MemoryStream([1]), "a.png");

        Assert.Equal(MediaUploadOutcome.Created, result.Outcome);
        Assert.Equal(["blob:put", "row:create"], _calls);
    }

    [Fact]
    public async Task Upload_ThatFailsTheChecks_IsRejected_AndStoresNothing()
    {
        _cleaner.CleanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(MediaCheckResult.Reject(MediaRejections.NotAnImage));

        var result = await _sut.UploadAsync(new MemoryStream([1]), "a.png");

        Assert.Equal(MediaUploadOutcome.Rejected, result.Outcome);
        Assert.Equal(MediaRejections.NotAnImage, result.Rejection);
        Assert.Empty(_calls);
        await _assets.DidNotReceiveWithAnyArgs().FindBySha256Async(default!, default);
    }

    [Fact]
    public async Task Upload_WhoseRecordFailsToSave_DeletesTheStoredFile_AndRethrows()
    {
        _assets.CreateAsync(Arg.Any<NewMediaAsset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UploadAsync(new MemoryStream([1]), "a.png"));

        await _blobs.Received(1).DeleteAsync(Arg.Is<string>(k => k.StartsWith("originals/")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_StillRethrowsTheSaveFailure_WhenTheCleanUpFailsToo()
    {
        _assets.CreateAsync(Arg.Any<NewMediaAsset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database down"));
        _blobs.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("storage down"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UploadAsync(new MemoryStream([1]), "a.png"));

        Assert.Equal("database down", thrown.Message);
    }

    [Fact]
    public async Task Replace_StoresTheNextVersionThenRecordsIt()
    {
        var id = Guid.NewGuid();
        _assets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(id, currentVersion: 3));
        _assets.AddVersionAsync(id, 4, Arg.Any<NewMediaAssetVersion>(), Arg.Any<CancellationToken>())
            .Returns(VersionDto(id, 4)).AndDoes(_ => _calls.Add("row:version"));

        var result = await _sut.ReplaceAsync(id, new MemoryStream([1]));

        Assert.Equal(MediaReplaceOutcome.Replaced, result.Outcome);
        Assert.Equal(4, result.Version!.Version);
        Assert.Equal(["blob:put", "row:version"], _calls);
        await _blobs.Received(1).PutAsync(MediaBlobKeys.Original(id, 4), Arg.Any<Stream>(), "image/png", Arg.Any<CancellationToken>());
        await _assets.Received(1).AddVersionAsync(id, 4, Arg.Is<NewMediaAssetVersion>(v =>
            v.BlobKey == MediaBlobKeys.Original(id, 4) && v.Sha256 == CleanSha), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replace_OfAnUnknownImage_IsNotFound_AndStoresNothing()
    {
        var result = await _sut.ReplaceAsync(Guid.NewGuid(), new MemoryStream([1]));

        Assert.Equal(MediaReplaceOutcome.NotFound, result.Outcome);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Replace_ThatFailsTheChecks_IsRejected()
    {
        var id = Guid.NewGuid();
        _assets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(id));
        _cleaner.CleanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(MediaCheckResult.Reject(MediaRejections.TooLarge));

        var result = await _sut.ReplaceAsync(id, new MemoryStream([1]));

        Assert.Equal(MediaReplaceOutcome.Rejected, result.Outcome);
        Assert.Equal(MediaRejections.TooLarge, result.Rejection);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Replace_WhenTheVersionFileIsAlreadyTaken_IsRejected_AndLeavesThatFileAlone()
    {
        var id = Guid.NewGuid();
        _assets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(id));
        _blobs.PutAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.ReplaceAsync(id, new MemoryStream([1]));

        Assert.Equal(MediaReplaceOutcome.Rejected, result.Outcome);
        Assert.Equal(MediaRejections.ReplacedConcurrently, result.Rejection);
        await _blobs.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        await _assets.DidNotReceiveWithAnyArgs().AddVersionAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Replace_ThatLosesARaceForTheVersion_DeletesItsFile_AndIsRejected()
    {
        var id = Guid.NewGuid();
        _assets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(id));
        _assets.AddVersionAsync(id, 2, Arg.Any<NewMediaAssetVersion>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MediaVersionConflictException(id, 2));

        var result = await _sut.ReplaceAsync(id, new MemoryStream([1]));

        Assert.Equal(MediaReplaceResult.Rejected(MediaRejections.ReplacedConcurrently), result);
        await _blobs.Received(1).DeleteAsync(MediaBlobKeys.Original(id, 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replace_OfAnImageDeletedMeanwhile_DeletesItsFile_AndIsNotFound()
    {
        var id = Guid.NewGuid();
        _assets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(id));
        _assets.AddVersionAsync(id, 2, Arg.Any<NewMediaAssetVersion>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _sut.ReplaceAsync(id, new MemoryStream([1]));

        Assert.Equal(MediaReplaceOutcome.NotFound, result.Outcome);
        await _blobs.Received(1).DeleteAsync(MediaBlobKeys.Original(id, 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replace_WhoseRecordFailsToSave_DeletesItsFile_AndRethrows()
    {
        var id = Guid.NewGuid();
        _assets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(id));
        _assets.AddVersionAsync(id, 2, Arg.Any<NewMediaAssetVersion>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ReplaceAsync(id, new MemoryStream([1])));

        await _blobs.Received(1).DeleteAsync(MediaBlobKeys.Original(id, 2), Arg.Any<CancellationToken>());
    }

    // Cleaning decodes the whole image, so parallel uploads from the library page queue at the
    // gate instead of all decoding at once.
    [Fact]
    public async Task Uploads_BeyondTheLimit_WaitForACleanToFinish()
    {
        var release = new TaskCompletionSource();
        var started = 0;
        _cleaner.CleanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Interlocked.Increment(ref started);
            await release.Task;
            return MediaCheckResult.Reject(MediaRejections.SvgUnreadable);
        });

        var uploads = Enumerable.Range(0, 3).Select(_ => _sut.UploadAsync(new MemoryStream([1]), "a.png")).ToList();
        await Task.Delay(100);
        Assert.Equal(2, Volatile.Read(ref started));

        release.SetResult();
        await Task.WhenAll(uploads).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, Volatile.Read(ref started));
    }

    [Fact]
    public async Task Replaces_ShareTheSameLimit()
    {
        var id = Guid.NewGuid();
        _assets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Dto(id));
        var release = new TaskCompletionSource();
        var started = 0;
        _cleaner.CleanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Interlocked.Increment(ref started);
            await release.Task;
            return MediaCheckResult.Reject(MediaRejections.SvgUnreadable);
        });

        var work = new Task[]
        {
            _sut.UploadAsync(new MemoryStream([1]), "a.png"),
            _sut.ReplaceAsync(id, new MemoryStream([1])),
            _sut.ReplaceAsync(id, new MemoryStream([1]))
        };
        await Task.Delay(100);
        Assert.Equal(2, Volatile.Read(ref started));

        release.SetResult();
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ACleanThatThrows_GivesItsPlaceBack()
    {
        _cleaner.CleanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("read failed"));

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<IOException>(() => _sut.UploadAsync(new MemoryStream([1]), "a.png"));

        using var place = await _gate.EnterAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var second = await _gate.EnterAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
