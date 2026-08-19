using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// A dataset is an input to one exercise, not to the window (#314). The window DTO therefore holds
// exercises, and reaches its files only through them. AllDatasets is the transitional flattening
// the single-run ingest and the summary page still need; #316 and #319 make both per-exercise.
public class WindowExercisesTests
{
    [Fact]
    public void AllDatasets_flattens_across_exercises_in_exercise_then_dataset_order()
    {
        CheckingWindowDto window = Window();
        window.Exercises =
        [
            Exercise(CheckingExerciseType.ResultsEnquiry, sortOrder: 1, datasets: [Dataset("results", 0)]),
            Exercise(CheckingExerciseType.PupilData, sortOrder: 0,
                datasets: [Dataset("nonincluded", 1), Dataset("included", 0)])
        ];

        Assert.Equal(
            ["included", "nonincluded", "results"],
            window.AllDatasets.Select(d => d.Name));
    }

    [Fact]
    public void An_exercise_with_no_datasets_contributes_nothing_and_does_not_throw()
    {
        CheckingWindowDto window = Window();
        window.Exercises =
        [
            Exercise(CheckingExerciseType.PupilData, sortOrder: 0, datasets: [Dataset("pupils", 0)]),
            Exercise(CheckingExerciseType.ResultsEnquiry, sortOrder: 1, datasets: [])
        ];

        Assert.Equal("pupils", Assert.Single(window.AllDatasets).Name);
    }

    [Fact]
    public void FindDataset_finds_a_dataset_held_by_any_exercise()
    {
        CheckingWindowDto window = Window();
        window.Exercises =
        [
            Exercise(CheckingExerciseType.PupilData, sortOrder: 0, datasets: [Dataset("pupils", 0)]),
            Exercise(CheckingExerciseType.ResultsEnquiry, sortOrder: 1, datasets: [Dataset("results", 0)])
        ];

        Assert.Equal("results", window.FindDataset("results")!.Name);
        Assert.Null(window.FindDataset("nothing-by-this-name"));
    }

    [Fact]
    public async Task CreateAsync_gives_a_new_window_a_pupil_data_exercise_on_the_window_dates()
    {
        (WindowService service, Func<CheckingWindowDto?> persisted) = ServiceCapturingCreate();

        CheckingWindowDto window = Window();
        window.StartDate = new DateTime(2027, 1, 1, 9, 0, 0);
        window.EndDate = new DateTime(2027, 1, 14, 17, 0, 0);

        await service.CreateAsync(window, CancellationToken.None);

        CheckingExerciseDto exercise = Assert.Single(persisted()!.Exercises);
        Assert.Equal(CheckingExerciseType.PupilData, exercise.ExerciseType);
        Assert.Equal(new DateTime(2027, 1, 1, 9, 0, 0), exercise.StartDate);
        Assert.Equal(new DateTime(2027, 1, 14, 17, 0, 0), exercise.EndDate);
    }

    [Fact]
    public async Task The_pupil_data_exercise_holds_the_dataset_slots_the_window_type_requires()
    {
        (WindowService service, Func<CheckingWindowDto?> persisted) = ServiceCapturingCreate();

        CheckingWindowDto window = Window();
        window.CheckingWindowType = CheckingWindowType.Post16;

        await service.CreateAsync(window, CancellationToken.None);

        Assert.Equal(
            ["included", "nonincluded"],
            Assert.Single(persisted()!.Exercises).Datasets.Select(d => d.Name));
    }

    [Fact]
    public async Task A_slot_that_survives_a_window_type_change_keeps_the_file_already_uploaded_to_it()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        CheckingWindowDto? persisted = null;
        await repository.UpdateAsync(
            Arg.Do<CheckingWindowDto>(w => persisted = w), Arg.Any<CancellationToken>());
        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window();
        window.CheckingWindowType = CheckingWindowType.KS2;
        window.Exercises =
        [
            Exercise(CheckingExerciseType.PupilData, sortOrder: 0,
                datasets: [new CheckingWindowDatasetDto
                {
                    Name = "pupils", SortOrder = 0, IngressFile = "already-uploaded.csv"
                }])
        ];

        await service.UpdateAsync(window, CancellationToken.None);

        Assert.Equal(
            "already-uploaded.csv",
            Assert.Single(Assert.Single(persisted!.Exercises).Datasets).IngressFile);
    }

    // The dataset slots follow the window type, so switching type replaces slots the new type does
    // not have. Post16 supplies two named files; KS2 supplies one under a different name.
    [Fact]
    public async Task Changing_the_window_type_replaces_the_slots_the_new_type_does_not_use()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        CheckingWindowDto? persisted = null;
        await repository.UpdateAsync(
            Arg.Do<CheckingWindowDto>(w => persisted = w), Arg.Any<CancellationToken>());
        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window();
        window.CheckingWindowType = CheckingWindowType.Post16;
        window.Exercises =
        [
            Exercise(CheckingExerciseType.PupilData, sortOrder: 0, datasets: [Dataset("pupils", 0)])
        ];

        await service.UpdateAsync(window, CancellationToken.None);

        Assert.Equal(
            ["included", "nonincluded"],
            Assert.Single(persisted!.Exercises).Datasets.Select(d => d.Name));
    }

    private static (WindowService, Func<CheckingWindowDto?>) ServiceCapturingCreate()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        CheckingWindowDto? persisted = null;
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                persisted = call.Arg<CheckingWindowDto>();
                return persisted;
            });
        return (new WindowService(repository, TimeProvider.System), () => persisted);
    }

    private static CheckingExerciseDto Exercise(
        CheckingExerciseType type, int sortOrder, List<CheckingWindowDatasetDto> datasets) =>
        new()
        {
            ExerciseType = type,
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 1, 14),
            SortOrder = sortOrder,
            Datasets = datasets
        };

    private static CheckingWindowDatasetDto Dataset(string name, int sortOrder) =>
        new() { Name = name, SortOrder = sortOrder };

    private static CheckingWindowDto Window() =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "A window",
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 1, 14),
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2
        };
}
