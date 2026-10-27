using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using NSubstitute;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class CheckingExerciseIngressTests
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly ICheckingExerciseDefinitionRepository _definitions =
        Substitute.For<ICheckingExerciseDefinitionRepository>();
    private readonly ICsvSchemaFileProcessor _processor = Substitute.For<ICsvSchemaFileProcessor>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(new DateTime(2026, 6, 1), TimeSpan.Zero));

    private CheckingExerciseIngress Ingress() => new(_definitions, _processor, _clock);

    private static CheckingExerciseDto Complete(Guid id) => new()
    {
        Id = id, ExerciseType = CheckingExerciseType.PupilData,
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

    private static async IAsyncEnumerable<ValidationProgress> Progress(ValidationProgress progress)
    {
        yield return progress;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AnExerciseMissingARequiredFile_IsRefusedWithoutRunning()
    {
        var id = Guid.NewGuid();
        var exercise = Complete(id);
        exercise.Datasets[0].IngressFile = string.Empty;
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), exercise));

        var progress = await Ingress().ProcessAsync(id).ToListAsync();

        Assert.True(progress.Single().IsError);
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default!).ToListAsync();
    }

    [Fact]
    public async Task AnUnknownExercise_IsRefused()
    {
        _definitions.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((CheckingExerciseDefinition?)null);

        Assert.True((await Ingress().ProcessAsync(Guid.NewGuid()).ToListAsync()).Single().IsError);
    }

    [Fact]
    public async Task ACleanRun_StampsTheExerciseWithTheChecksumsItRanOver()
    {
        var id = Guid.NewGuid();
        var exercise = Complete(id);
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), exercise));
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>())
            .Returns(Progress(new("Done", "", 1, 1, 0, 1, true, false)));

        await Ingress().ProcessAsync(id).ToListAsync();

        await _definitions.Received(1).StampAsync(id, Arg.Any<DateTime>(),
            exercise.CurrentIngressChecksum, exercise.CurrentSchemaChecksum, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARunThatEndsInError_StampsNothing()
    {
        var id = Guid.NewGuid();
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), Complete(id)));
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>())
            .Returns(Progress(new("Failed", "bad row", 1, 0, 1, 1, true, true)));

        await Ingress().ProcessAsync(id).ToListAsync();

        await _definitions.DidNotReceiveWithAnyArgs().StampAsync(default, default, default!, default!, default);
    }
}
