using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.UnitTests.Common;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class IngressSummaryLogTests
{
    private readonly RecordingLogger<IngressSummaryStore> _storeLog = new();
    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();

    private IngressSummaryStore StoreWithNoClient() =>
        new(new Dictionary<string, BlobServiceClient>(), _storeLog);

    private IngressSummaryStore StoreThatStorageRefuses()
    {
        var failure = new RequestFailedException(500, "A failure that the test injects.");
        var blob = Substitute.For<BlobClient>();
        blob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response<bool>>(failure));
        blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response<BlobContentInfo>>(failure));
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(blob);
        var service = Substitute.For<BlobServiceClient>();
        service.GetBlobContainerClient(Arg.Any<string>()).Returns(container);
        return new(new Dictionary<string, BlobServiceClient> { ["app"] = service }, _storeLog);
    }

    private static void AssertOneError(IReadOnlyList<RecordedLog> entries, string message)
    {
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(message, entry.Message);
    }

    [Fact]
    public async Task AnArchiveWithNoStorageClient_LogsTheClearFailedCode()
    {
        var archived = await StoreWithNoClient().ArchiveCurrentAsync(_windowId, _exerciseId, CancellationToken.None);

        Assert.False(archived);
        AssertOneError(_storeLog.Entries,
            $"Ingress summary storage is unavailable (summary_clear_failed) for exercise {_exerciseId}");
    }

    [Fact]
    public async Task AnArchiveThatStorageRefuses_LogsTheClearFailedCode()
    {
        var archived = await StoreThatStorageRefuses()
            .ArchiveCurrentAsync(_windowId, _exerciseId, CancellationToken.None);

        Assert.False(archived);
        AssertOneError(_storeLog.Entries,
            $"Ingress summary archival failed (summary_clear_failed) for exercise {_exerciseId}");
    }

    [Fact]
    public async Task AWriteWithNoStorageClient_LogsTheWriteFailedCode()
    {
        var written = await StoreWithNoClient()
            .WriteAsync(_windowId, _exerciseId, new IngressSummaryDocument(), CancellationToken.None);

        Assert.False(written);
        AssertOneError(_storeLog.Entries,
            $"Ingress summary storage is unavailable (summary_write_failed) for exercise {_exerciseId}");
    }

    [Fact]
    public async Task AWriteThatStorageRefuses_LogsTheWriteFailedCode()
    {
        var written = await StoreThatStorageRefuses()
            .WriteAsync(_windowId, _exerciseId, new IngressSummaryDocument(), CancellationToken.None);

        Assert.False(written);
        AssertOneError(_storeLog.Entries,
            $"Ingress summary write failed (summary_write_failed) for exercise {_exerciseId}");
    }

    [Fact]
    public async Task ARunWhoseSummaryWriteThrows_LogsTheWriteFailedCode()
    {
        var ingressLog = new RecordingLogger<CheckingExerciseIngress>();
        var definitions = Substitute.For<ICheckingExerciseDefinitionRepository>();
        definitions.GetAsync(_exerciseId, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(_windowId, new CheckingExerciseDto
            {
                Id = _exerciseId, ExerciseType = CheckingExerciseType.PupilData,
                StartDate = DateTime.Today, EndDate = DateTime.Today, UsesExerciseStorage = true,
                Datasets = []
            }));
        var summaryStore = Substitute.For<IIngressSummaryStore>();
        summaryStore.ArchiveCurrentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        summaryStore.WriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IngressSummaryDocument>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("storage is down")));
        var ingress = new CheckingExerciseIngress(definitions, Substitute.For<ICsvSchemaFileProcessor>(),
            TimeProvider.System, DfE.CheckPerformanceData.Application.UnitTests.Common.NoSampleSchools.Repository(), summaryStore, ingressLog);

        await ingress.ProcessAsync(_exerciseId).ToListAsync();

        AssertOneError(ingressLog.Entries,
            $"Ingress summary write failed (summary_write_failed) for exercise {_exerciseId}");
    }
}
