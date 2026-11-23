using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class CheckingExerciseIngressFailureSummaryTests
{
    private static readonly ValidationProgress CleanRun = new("Done", "", 1, 1, 1, 0, true, false);
    private static readonly ValidationProgress FailedRun = new("Failed", "bad row", 1, 0, 0, 1, true, true);

    private readonly ICheckingExerciseDefinitionRepository _definitions =
        Substitute.For<ICheckingExerciseDefinitionRepository>();
    private readonly ICsvSchemaFileProcessor _processor = Substitute.For<ICsvSchemaFileProcessor>();
    private readonly IIngressSummaryStore _summaryStore = Substitute.For<IIngressSummaryStore>();
    private readonly Guid _exerciseId = Guid.NewGuid();
    private IngressSummaryDocument? _written;

    public CheckingExerciseIngressFailureSummaryTests()
    {
        _summaryStore.ArchiveCurrentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _summaryStore.WriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Do<IngressSummaryDocument>(summary => _written = summary), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private CheckingExerciseDto Exercise() => new()
    {
        Id = _exerciseId, ExerciseType = CheckingExerciseType.PupilData,
        StartDate = DateTime.Today, EndDate = DateTime.Today, UsesExerciseStorage = true,
        Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "pupils", IngressFile = "a.csv", IngressFileChecksum = "1",
                SchemaFile = "a.json", SchemaFileChecksum = "2"
            }
        ]
    };

    private async Task RunAsync(CheckingExerciseDto exercise)
    {
        _definitions.GetAsync(_exerciseId, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), exercise));
        var ingress = new CheckingExerciseIngress(_definitions, _processor, TimeProvider.System, _summaryStore);

        await ingress.ProcessAsync(_exerciseId).ToListAsync();
    }

    private void ProcessorRuns(Func<IngressSummaryCapture, IAsyncEnumerable<ValidationProgress>> run) =>
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>(), Arg.Any<Guid?>(),
                Arg.Any<IngressSummaryCapture?>())
            .Returns(call => run(call.Arg<IngressSummaryCapture>()));

    private void PublicationThrows(Exception exception) =>
        _definitions.PublishReleaseAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseReleaseDto>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CheckingExerciseReleaseDto>(exception));

    private static async IAsyncEnumerable<ValidationProgress> Yields(ValidationProgress progress)
    {
        yield return progress;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ValidationProgress> Throws(Exception exception)
    {
        await Task.FromException(exception);
        yield break;
    }

    private void AssertSummary(string outcome, string stage, string code)
    {
        var written = Assert.IsType<IngressSummaryDocument>(_written);
        Assert.Equal(outcome, written.Outcome);
        Assert.Equal(stage, written.Failure?.Stage);
        Assert.Equal(code, written.Failure?.Code);
    }

    [Fact]
    public async Task AnExerciseWithNoDatasets_WritesAConfigurationFailure()
    {
        var exercise = Exercise();
        exercise.Datasets = [];

        await RunAsync(exercise);

        AssertSummary("failed", "configuration", "no_datasets");
    }

    [Fact]
    public async Task AnExerciseMissingARequiredFile_WritesAConfigurationFailure()
    {
        var exercise = Exercise();
        exercise.Datasets[0].IngressFile = string.Empty;

        await RunAsync(exercise);

        AssertSummary("failed", "configuration", "required_file_missing");
    }

    [Fact]
    public async Task ARunThatIsCancelled_WritesACancelledFailure()
    {
        ProcessorRuns(_ => Throws(new OperationCanceledException()));

        await RunAsync(Exercise());

        AssertSummary("cancelled", "cancelled", "operation_cancelled");
    }

    [Fact]
    public async Task ARunThatThrows_WritesAnInputReadFailure()
    {
        ProcessorRuns(_ => Throws(new InvalidOperationException("storage is down")));

        await RunAsync(Exercise());

        AssertSummary("failed", "input_read", "input_read_failed");
    }

    [Fact]
    public async Task APublicationThatIsCancelled_WritesACancelledFailure()
    {
        ProcessorRuns(_ => Yields(CleanRun));
        PublicationThrows(new OperationCanceledException());

        await RunAsync(Exercise());

        AssertSummary("cancelled", "cancelled", "operation_cancelled");
    }

    [Fact]
    public async Task APublicationThatThrows_WritesAPublicationFailure()
    {
        ProcessorRuns(_ => Yields(CleanRun));
        PublicationThrows(new InvalidOperationException("database is down"));

        await RunAsync(Exercise());

        AssertSummary("failed", "publication", "publication_failed");
    }

    [Fact]
    public async Task ARunThatEndsInErrorWithNoFailureSet_WritesAValidationFailure()
    {
        ProcessorRuns(_ => Yields(FailedRun));

        await RunAsync(Exercise());

        AssertSummary("failed", "validation", "record_validation_failed");
    }

    [Fact]
    public async Task ARunThatEndsInErrorWithTheCancelledStage_HasTheCancelledOutcome()
    {
        ProcessorRuns(capture =>
        {
            capture.SetFailure("cancelled", "operation_cancelled");
            return Yields(FailedRun);
        });

        await RunAsync(Exercise());

        AssertSummary("cancelled", "cancelled", "operation_cancelled");
    }

    [Fact]
    public async Task ARunThatEndsInErrorWithItsOwnFailure_KeepsThatFailure()
    {
        ProcessorRuns(capture =>
        {
            capture.SetFailure("parsing", "malformed_row");
            return Yields(FailedRun);
        });

        await RunAsync(Exercise());

        AssertSummary("failed", "parsing", "malformed_row");
    }
}
