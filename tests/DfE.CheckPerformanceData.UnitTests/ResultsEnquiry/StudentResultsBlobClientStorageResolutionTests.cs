using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// #466 slice: StudentResultsBlobClient mirrors PupilDataBlobClient's move to ask
// ICheckingExerciseStorageResolver which exercise row owns a window's storage. A null resolver
// (the constructor default) must reproduce the legacy path exactly, because every window
// configured before #466 depends on it.
public sealed class StudentResultsBlobClientStorageResolutionTests
{
    private readonly BlobServiceClient _service = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _container = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blob = Substitute.For<BlobClient>();
    private readonly ICheckingExerciseStorageResolver _resolver = Substitute.For<ICheckingExerciseStorageResolver>();
    private string? _lastBlobNameRequested;

    private StudentResultsBlobClient Client(bool withResolver = true)
    {
        _service.GetBlobContainerClient(Arg.Any<string>()).Returns(_container);
        _container.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(true, Substitute.For<Azure.Response>()));
        _container.GetBlobClient(Arg.Do<string>(name => _lastBlobNameRequested = name)).Returns(_blob);
        _blob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(false, Substitute.For<Azure.Response>()));
        return new StudentResultsBlobClient(_service, new MemoryCache(new MemoryCacheOptions()), withResolver ? _resolver : null);
    }

    [Fact]
    public async Task ReadsFromTheResolvedExercisesOwnStorage()
    {
        var windowId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        _resolver.ResolveAsync(windowId, CheckingExerciseType.ResultsEnquiry, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDto
            {
                Id = exerciseId,
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today,
                UsesExerciseStorage = true
            });

        await Client().GetResultsAsync(windowId, "933/4290", "cypmd-1");

        Assert.Equal($"exercises/{exerciseId}/data/9334290_results.json", _lastBlobNameRequested);
    }

    [Fact]
    public async Task AnAmbiguousWindow_ReadsNothingRatherThanGuessing()
    {
        _resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType>(), Arg.Any<CancellationToken>())
            .Returns((CheckingExerciseDto?)null);

        var results = await Client().GetResultsAsync(Guid.NewGuid(), "933/4290", "cypmd-1");

        Assert.Empty(results);
        // Never even asked the container for a blob, let alone a wrong one.
        _container.DidNotReceive().GetBlobClient(Arg.Any<string>());
    }

    [Fact]
    public async Task AnAmbiguousWindow_UploadResultsAsyncThrowsRatherThanScatteringFiles()
    {
        _resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType>(), Arg.Any<CancellationToken>())
            .Returns((CheckingExerciseDto?)null);
        _container.CreateIfNotExistsAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns((Azure.Response<Azure.Storage.Blobs.Models.BlobContainerInfo>?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client().UploadResultsAsync(Guid.NewGuid(), "933/4290", []));
    }

    [Fact]
    public async Task ARowThatDoesNotUseExerciseStorage_FallsBackToTheLegacyKindPath()
    {
        var windowId = Guid.NewGuid();
        _resolver.ResolveAsync(windowId, CheckingExerciseType.ResultsEnquiry, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDto
            {
                Id = Guid.NewGuid(),
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today,
                UsesExerciseStorage = false
            });

        await Client().GetResultsAsync(windowId, "933/4290", "cypmd-1");

        Assert.Equal("results-enquiry/data/9334290_results.json", _lastBlobNameRequested);
    }

    [Fact]
    public async Task ANullResolver_IsByteIdenticalToTheLegacyPath()
    {
        var windowId = Guid.NewGuid();

        await Client(withResolver: false).GetResultsAsync(windowId, "933/4290", "cypmd-1");

        Assert.Equal("results-enquiry/data/9334290_results.json", _lastBlobNameRequested);
        _resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, default);
    }
}
