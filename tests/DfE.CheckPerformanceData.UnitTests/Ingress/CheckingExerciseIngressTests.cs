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

    private void ProcessorReturns(ValidationProgress progress) =>
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>(), Arg.Any<Guid?>())
            .Returns(Progress(progress));

    [Fact]
    public async Task ACleanRun_PublishesARelease_WithTheChecksumsItRanOver()
    {
        var id = Guid.NewGuid();
        var exercise = Complete(id);
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), exercise));
        ProcessorReturns(new("Done", "", 1, 1, 3, 0, true, false));

        await Ingress().ProcessAsync(id, publishedBy: "admin@example.gov.uk").ToListAsync();

        await _definitions.Received(1).PublishReleaseAsync(id,
            Arg.Is<CheckingExerciseReleaseDto>(r =>
                r.PublishedBy == "admin@example.gov.uk"
                && r.FilesWritten == 3
                && r.PublishedAt == new DateTime(2026, 6, 1)
                && r.Files.Single().DatasetName == "pupils"
                && r.Files.Single().IngressFile == "a.csv"
                && r.Files.Single().SchemaFile == "a.json"),
            exercise.CurrentIngressChecksum, exercise.CurrentSchemaChecksum, Arg.Any<CancellationToken>());
        await _definitions.DidNotReceiveWithAnyArgs().StampAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task ARun_WritesUnderTheSameNewReleaseItThenPublishes()
    {
        // The output prefix and the release row must name the same release, or the exercise would
        // switch to a prefix with nothing in it.
        var id = Guid.NewGuid();
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), Complete(id)));
        Guid? written = null;
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), id, Arg.Any<CheckingDataType?>(), Arg.Do<Guid?>(r => written = r))
            .Returns(Progress(new("Done", "", 1, 1, 1, 0, true, false)));

        await Ingress().ProcessAsync(id).ToListAsync();

        Assert.NotNull(written);
        await _definitions.Received(1).PublishReleaseAsync(id,
            Arg.Is<CheckingExerciseReleaseDto>(r => r.Id == written), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachRun_WritesUnderADifferentRelease()
    {
        var id = Guid.NewGuid();
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), Complete(id)));
        var releases = new List<Guid?>();
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>(), Arg.Do<Guid?>(releases.Add))
            .Returns(_ => Progress(new("Done", "", 1, 1, 1, 0, true, false)));

        await Ingress().ProcessAsync(id).ToListAsync();
        await Ingress().ProcessAsync(id).ToListAsync();

        Assert.Equal(2, releases.Distinct().Count());
        Assert.All(releases, r => Assert.NotNull(r));
    }

    [Fact]
    public async Task ALegacyRow_IsStampedWithoutARelease()
    {
        // A row on the kind-based paths: its readers do not know the release prefix.
        var id = Guid.NewGuid();
        var legacy = new CheckingExerciseDto
        {
            Id = id, ExerciseType = CheckingExerciseType.PupilData, UsesExerciseStorage = false,
            StartDate = DateTime.Today, EndDate = DateTime.Today, Datasets = Complete(id).Datasets
        };
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), legacy));
        Guid? written = Guid.Empty;
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>(), Arg.Do<Guid?>(r => written = r))
            .Returns(Progress(new("Done", "", 1, 1, 1, 0, true, false)));

        await Ingress().ProcessAsync(id).ToListAsync();

        Assert.Null(written);
        await _definitions.Received(1).StampAsync(id, Arg.Any<DateTime>(),
            legacy.CurrentIngressChecksum, legacy.CurrentSchemaChecksum, Arg.Any<CancellationToken>());
        await _definitions.DidNotReceiveWithAnyArgs().PublishReleaseAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task ARunThatEndsInError_StampsNothing()
    {
        var id = Guid.NewGuid();
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), Complete(id)));
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Any<IReadOnlyList<IngressDataset>>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>(), Arg.Any<Guid?>())
            .Returns(Progress(new("Failed", "bad row", 1, 0, 1, 1, true, true)));

        await Ingress().ProcessAsync(id).ToListAsync();

        await _definitions.DidNotReceiveWithAnyArgs().StampAsync(default, default, default!, default!, default);
        await _definitions.DidNotReceiveWithAnyArgs().PublishReleaseAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task ARun_PassesEachSlotsIdAndJourneyFlagToTheProcessorAndTheRelease()
    {
        var id = Guid.NewGuid();
        var journeySlot = Guid.NewGuid();
        var shareSlot = Guid.NewGuid();
        var exercise = Complete(id);
        exercise.Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Id = journeySlot, Name = "pupils", FeedsJourney = true, IngressFile = "a.csv",
                IngressFileChecksum = "1", SchemaFile = "a.json", SchemaFileChecksum = "2", SortOrder = 0
            },
            new CheckingWindowDatasetDto
            {
                Id = shareSlot, Name = "share", FeedsJourney = false, Required = false, IngressFile = "b.csv",
                IngressFileChecksum = "3", SchemaFile = "b.json", SchemaFileChecksum = "4", SortOrder = 1
            }
        ];
        _definitions.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(Guid.NewGuid(), exercise));
        IReadOnlyList<IngressDataset>? inputs = null;
        _processor.ProcessAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType?>(),
                Arg.Do<IReadOnlyList<IngressDataset>>(d => inputs = d), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Guid?>(), Arg.Any<CheckingDataType?>(), Arg.Any<Guid?>())
            .Returns(Progress(new("Done", "", 1, 1, 1, 0, true, false)));

        await Ingress().ProcessAsync(id).ToListAsync();

        Assert.Equal([(journeySlot, true), (shareSlot, false)], inputs!.Select(d => (d.DatasetId, d.FeedsJourney)));
        await _definitions.Received(1).PublishReleaseAsync(id,
            Arg.Is<CheckingExerciseReleaseDto>(r =>
                r.Files.Select(f => f.DatasetId).SequenceEqual(new[] { journeySlot, shareSlot })
                && r.Files.Select(f => f.FeedsJourney).SequenceEqual(new[] { true, false })),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
