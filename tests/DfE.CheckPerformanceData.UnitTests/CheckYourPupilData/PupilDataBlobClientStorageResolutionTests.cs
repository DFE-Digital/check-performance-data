using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// #466 slice: PupilDataBlobClient must ask ICheckingExerciseStorageResolver which exercise row
// owns a window's storage before naming a blob path, rather than always deriving the path from
// the exercise kind. A null resolver (the constructor default) must reproduce the legacy
// behaviour byte-for-byte, because every window configured before #466 — and every existing
// caller that builds the client with a blob service alone — depends on that.
public sealed class PupilDataBlobClientStorageResolutionTests
{
    private readonly BlobServiceClient _service = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _container = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blob = Substitute.For<BlobClient>();
    private readonly ICheckingExerciseStorageResolver _resolver = Substitute.For<ICheckingExerciseStorageResolver>();
    private string? _lastBlobNameRequested;

    private PupilDataBlobClient Client(bool withResolver = true)
    {
        _service.GetBlobContainerClient(Arg.Any<string>()).Returns(_container);
        _container.GetBlobClient(Arg.Do<string>(name => _lastBlobNameRequested = name)).Returns(_blob);
        return new PupilDataBlobClient(_service, withResolver ? _resolver : null);
    }

    [Fact]
    public async Task ReadsFromTheResolvedExercisesOwnStorage()
    {
        var windowId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        _resolver.ResolveAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDto
            {
                Id = exerciseId,
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today,
                UsesExerciseStorage = true
            });

        await Client().HasPupilDataAsync(windowId, CheckingExerciseType.PupilData, "933/4290");

        Assert.Equal($"exercises/{exerciseId}/data/9334290_pupils.json", _lastBlobNameRequested);
    }

    [Fact]
    public async Task AnAmbiguousWindow_ReadsNothingRatherThanGuessing()
    {
        _resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType>(), Arg.Any<CancellationToken>())
            .Returns((CheckingExerciseDto?)null);

        Assert.False(await Client().HasPupilDataAsync(Guid.NewGuid(), CheckingExerciseType.PupilData, "933/4290"));
    }

    [Fact]
    public async Task AnAmbiguousWindow_GetPupilsAsyncReturnsNullRatherThanGuessing()
    {
        _resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType>(), Arg.Any<CancellationToken>())
            .Returns((CheckingExerciseDto?)null);

        var result = await Client().GetPupilsAsync(
            Guid.NewGuid(), CheckingExerciseType.PupilData, "933/4290", CheckingWindowType.KS4June);

        Assert.Null(result);
        // Never even asked the container for a blob, let alone a wrong one.
        _container.DidNotReceive().GetBlobClient(Arg.Any<string>());
    }

    [Fact]
    public async Task AnAmbiguousWindow_UploadPupilsAsyncThrowsRatherThanScatteringFiles()
    {
        _resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType>(), Arg.Any<CancellationToken>())
            .Returns((CheckingExerciseDto?)null);
        _container.CreateIfNotExistsAsync().Returns((Azure.Response<Azure.Storage.Blobs.Models.BlobContainerInfo>?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client().UploadPupilsAsync(
                Guid.NewGuid(), CheckingExerciseType.PupilData, "933/4290",
                new List<Application.CheckYourPupilData.PupilRecord>()));
    }

    [Fact]
    public async Task ARowThatDoesNotUseExerciseStorage_FallsBackToTheLegacyKindPath()
    {
        var windowId = Guid.NewGuid();
        _resolver.ResolveAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDto
            {
                Id = Guid.NewGuid(),
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today,
                UsesExerciseStorage = false
            });

        await Client().HasPupilDataAsync(windowId, CheckingExerciseType.PupilData, "933/4290");

        Assert.Equal("data/9334290_pupils.json", _lastBlobNameRequested);
    }

    [Fact]
    public async Task ANullResolver_IsByteIdenticalToTheLegacyPath()
    {
        var windowId = Guid.NewGuid();

        await Client(withResolver: false).HasPupilDataAsync(windowId, CheckingExerciseType.PupilData, "933/4290");

        Assert.Equal("data/9334290_pupils.json", _lastBlobNameRequested);
        // Never even constructed, let alone called: a null resolver must not be reached.
        _resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, default);
    }
}
