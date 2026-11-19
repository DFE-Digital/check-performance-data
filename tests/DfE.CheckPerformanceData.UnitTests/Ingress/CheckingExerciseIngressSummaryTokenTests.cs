using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class CheckingExerciseIngressSummaryTokenTests
{
    private static readonly ValidationProgress CleanRun = new("Done", "", 1, 1, 1, 0, true, false);

    private readonly ICheckingExerciseDefinitionRepository _definitions =
        Substitute.For<ICheckingExerciseDefinitionRepository>();
    private readonly ICsvSchemaFileProcessor _processor = Substitute.For<ICsvSchemaFileProcessor>();
    private readonly IIngressSummaryStore _summaryStore = Substitute.For<IIngressSummaryStore>();
    private readonly Guid _exerciseId = Guid.NewGuid();
    private CancellationToken? _writeToken;

    public CheckingExerciseIngressSummaryTokenTests()
    {
        _definitions.GetAsync(_exerciseId, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), new CheckingExerciseDto
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
            }));
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>(), Arg.Any<Guid?>(),
                Arg.Any<IngressSummaryCapture?>())
            .Returns(_ => Yields(CleanRun));
        _summaryStore.ArchiveCurrentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _summaryStore.WriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IngressSummaryDocument>(),
                Arg.Do<CancellationToken>(token => _writeToken = token))
            .Returns(true);
    }

    private void PublicationThrows(Exception exception) =>
        _definitions.PublishReleaseAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseReleaseDto>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CheckingExerciseReleaseDto>(exception));

    private async Task RunAsync(CancellationToken token)
    {
        var ingress = new CheckingExerciseIngress(_definitions, _processor, TimeProvider.System, _summaryStore);

        await ingress.ProcessAsync(_exerciseId, token).ToListAsync();
    }

    private static async IAsyncEnumerable<ValidationProgress> Yields(ValidationProgress progress)
    {
        yield return progress;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task APublicationThatIsCancelled_WritesTheSummaryWithNoCancellation()
    {
        using var source = new CancellationTokenSource();
        PublicationThrows(new OperationCanceledException());

        await RunAsync(source.Token);

        Assert.Equal(CancellationToken.None, _writeToken);
    }

    [Fact]
    public async Task APublicationThatThrows_WritesTheSummaryWithTheTokenOfTheRun()
    {
        using var source = new CancellationTokenSource();
        PublicationThrows(new InvalidOperationException("database is down"));

        await RunAsync(source.Token);

        Assert.Equal(source.Token, _writeToken);
    }
}
