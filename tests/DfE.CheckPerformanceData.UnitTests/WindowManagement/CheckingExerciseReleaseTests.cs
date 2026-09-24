using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// Each clean run of an exercise is a release with its own output. The exercise names the release
// schools see; the earlier ones stay, so a full replacement of the data never loses the old data.
public sealed class CheckingExerciseReleaseTests
{
    private static readonly Guid WindowId = Guid.NewGuid();

    private static CheckingExerciseReleaseDto Release(int number, string schema) => new()
    {
        Id = Guid.NewGuid(),
        Number = number,
        PublishedAt = new DateTime(2026, 1, number),
        Files =
        [
            new CheckingExerciseReleaseFileDto
            {
                DatasetName = "included", Included = true, IngressFile = $"r{number}.csv",
                IngressFileChecksum = $"c{number}", SchemaFile = schema, SchemaFileChecksum = $"s{number}"
            }
        ]
    };

    private static CheckingExerciseDto Exercise(Guid? current, params CheckingExerciseReleaseDto[] releases) => new()
    {
        Id = Guid.NewGuid(),
        ExerciseType = CheckingExerciseType.ResultsEnquiry,
        StartDate = new DateTime(2026, 1, 1),
        EndDate = new DateTime(2026, 6, 1),
        CurrentReleaseId = current,
        Releases = [.. releases],
        Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "included", Included = true, IngressFile = "new.csv", IngressFileChecksum = "n",
                SchemaFile = "new.json", SchemaFileChecksum = "ns"
            }
        ]
    };

    [Fact]
    public void AnExerciseWithNoRelease_ShowsItsSlots()
    {
        // Every exercise before releases existed, and every dev-seeded one.
        var exercise = Exercise(null);

        Assert.Null(exercise.CurrentRelease);
        Assert.Equal("new.json", Assert.Single(exercise.PublishedDatasets).SchemaFile);
    }

    [Fact]
    public void AnExerciseWithARelease_ShowsTheReleasesFiles_NotANewerUploadIntoTheSlot()
    {
        // The admin has uploaded the revised file and its schema but not run them yet. The title in
        // that schema must not reach schools before the data does.
        var first = Release(1, "first.json");
        var exercise = Exercise(first.Id, first);

        var shown = Assert.Single(exercise.PublishedDatasets);

        Assert.Equal("first.json", shown.SchemaFile);
        Assert.Equal("r1.csv", shown.IngressFile);
        Assert.Equal("included", shown.Name);
        Assert.True(shown.Included);
    }

    [Fact]
    public void CurrentRelease_IsTheOneTheExerciseNames_NotTheLatest()
    {
        // An admin made release 1 live again after release 2.
        var first = Release(1, "first.json");
        var second = Release(2, "second.json");

        var exercise = Exercise(first.Id, first, second);

        Assert.Same(first, exercise.CurrentRelease);
    }

    private readonly ICheckingExerciseDefinitionRepository _definitions =
        Substitute.For<ICheckingExerciseDefinitionRepository>();

    private CheckingExerciseReleaseService Service() => new(_definitions);

    private void Holds(Guid windowId, CheckingExerciseDto exercise) =>
        _definitions.GetAsync(exercise.Id, Arg.Any<CancellationToken>())
            .Returns(new CheckingExerciseDefinition(windowId, exercise));

    [Fact]
    public async Task MakeLive_SwitchesToAnEarlierRelease()
    {
        var first = Release(1, "first.json");
        var second = Release(2, "second.json");
        var exercise = Exercise(second.Id, first, second);
        Holds(WindowId, exercise);
        _definitions.SetCurrentReleaseAsync(exercise.Id, first.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Service().MakeLiveAsync(WindowId, exercise.Id, first.Id, default);

        Assert.Equal(MakeReleaseLiveResult.MadeLive, result);
        await _definitions.Received(1).SetCurrentReleaseAsync(exercise.Id, first.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MakeLive_OnTheLiveRelease_ChangesNothing()
    {
        var first = Release(1, "first.json");
        var exercise = Exercise(first.Id, first);
        Holds(WindowId, exercise);

        var result = await Service().MakeLiveAsync(WindowId, exercise.Id, first.Id, default);

        Assert.Equal(MakeReleaseLiveResult.AlreadyLive, result);
        await _definitions.DidNotReceiveWithAnyArgs().SetCurrentReleaseAsync(default, default, default);
    }

    [Fact]
    public async Task MakeLive_RefusesAnExerciseOfAnotherWindow()
    {
        // The window id comes from the route: this window's page must not change another's data.
        var first = Release(1, "first.json");
        var second = Release(2, "second.json");
        var exercise = Exercise(second.Id, first, second);
        Holds(Guid.NewGuid(), exercise);

        var result = await Service().MakeLiveAsync(WindowId, exercise.Id, first.Id, default);

        Assert.Equal(MakeReleaseLiveResult.NotFound, result);
        await _definitions.DidNotReceiveWithAnyArgs().SetCurrentReleaseAsync(default, default, default);
    }

    [Fact]
    public async Task MakeLive_RefusesAReleaseOfAnotherExercise()
    {
        var first = Release(1, "first.json");
        var exercise = Exercise(first.Id, first);
        Holds(WindowId, exercise);

        var result = await Service().MakeLiveAsync(WindowId, exercise.Id, Guid.NewGuid(), default);

        Assert.Equal(MakeReleaseLiveResult.NotFound, result);
        await _definitions.DidNotReceiveWithAnyArgs().SetCurrentReleaseAsync(default, default, default);
    }

    [Fact]
    public async Task MakeLive_RefusesAnUnknownExercise()
    {
        _definitions.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CheckingExerciseDefinition?)null);

        Assert.Equal(MakeReleaseLiveResult.NotFound,
            await Service().MakeLiveAsync(WindowId, Guid.NewGuid(), Guid.NewGuid(), default));
    }
}
