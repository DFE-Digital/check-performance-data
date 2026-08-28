using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// A dataset is an input to one exercise, not to the window (#314). The window DTO therefore holds
// exercises, and reaches its files only through them. #319 retired AllDatasets: the wizard, the
// summary page and the validate run all name the exercise they mean, so the flattening that made
// two exercises' datasets look like one list has no callers left.
public class WindowExercisesTests
{
    [Fact]
    public void FindExercise_returns_the_exercise_of_that_type()
    {
        CheckingWindowDto window = Window();
        window.Exercises =
        [
            Exercise(CheckingExerciseType.PupilData, sortOrder: 0, datasets: [Dataset("pupils", 0)]),
            Exercise(CheckingExerciseType.ResultsEnquiry, sortOrder: 1, datasets: [Dataset("results", 0)])
        ];

        Assert.Equal(
            "results",
            Assert.Single(window.FindExercise(CheckingExerciseType.ResultsEnquiry)!.Datasets).Name);
    }

    [Fact]
    public void FindExercise_returns_null_for_an_exercise_the_window_does_not_run()
    {
        CheckingWindowDto window = Window();
        window.Exercises = [Exercise(CheckingExerciseType.PupilData, sortOrder: 0, datasets: [])];

        Assert.Null(window.FindExercise(CheckingExerciseType.ResultsEnquiry));
    }

    // #319: the outer pair is derived, not typed, so it always equals the union of the exercises.
    [Fact]
    public void The_windows_dates_are_the_union_of_its_exercises_dates()
    {
        CheckingWindowDto window = Window();
        window.Exercises =
        [
            Dated(CheckingExerciseType.PupilData, new DateTime(2026, 10, 7), new DateTime(2026, 10, 18, 17, 0, 0), 0),
            Dated(CheckingExerciseType.ResultsEnquiry, new DateTime(2026, 10, 7), new DateTime(2027, 3, 31, 17, 0, 0), 1)
        ];

        window.DeriveDatesFromExercises();

        Assert.Equal(new DateTime(2026, 10, 7), window.StartDate);
        Assert.Equal(new DateTime(2027, 3, 31, 17, 0, 0), window.EndDate);
    }

    [Fact]
    public void The_windows_start_widens_when_a_second_exercise_starts_earlier()
    {
        CheckingWindowDto window = Window();
        window.Exercises =
        [
            Dated(CheckingExerciseType.PupilData, new DateTime(2026, 10, 7), new DateTime(2026, 10, 18), 0),
            Dated(CheckingExerciseType.ResultsEnquiry, new DateTime(2026, 9, 1), new DateTime(2026, 10, 10), 1)
        ];

        window.DeriveDatesFromExercises();

        Assert.Equal(new DateTime(2026, 9, 1), window.StartDate);
        Assert.Equal(new DateTime(2026, 10, 18), window.EndDate);
    }

    [Fact]
    public void A_window_with_no_exercises_keeps_the_dates_it_has()
    {
        CheckingWindowDto window = Window();

        window.DeriveDatesFromExercises();

        Assert.Equal(new DateTime(2027, 1, 1), window.StartDate);
        Assert.Equal(new DateTime(2027, 1, 14), window.EndDate);
    }

    [Fact]
    public async Task CreateAsync_derives_the_windows_dates_from_the_exercises_it_is_given()
    {
        (WindowService service, Func<CheckingWindowDto?> persisted) = ServiceCapturingCreate();

        CheckingWindowDto window = Window();
        window.CheckingWindowType = CheckingWindowType.Post16;
        window.Exercises =
        [
            Dated(CheckingExerciseType.PupilData, new DateTime(2026, 10, 7), new DateTime(2026, 10, 18, 17, 0, 0), 0),
            Dated(CheckingExerciseType.ResultsEnquiry, new DateTime(2026, 10, 7), new DateTime(2027, 3, 31, 17, 0, 0), 1)
        ];

        await service.CreateAsync(window, CancellationToken.None);

        Assert.Equal(new DateTime(2026, 10, 7), persisted()!.StartDate);
        Assert.Equal(new DateTime(2027, 3, 31, 17, 0, 0), persisted()!.EndDate);
    }

    // The admin chooses the exercises since #319, so a window that runs no pupil data checking must
    // not have one invented for it — that would silently undo their choice.
    [Fact]
    public async Task A_window_running_only_results_enquiry_does_not_gain_a_pupil_data_exercise()
    {
        (WindowService service, Func<CheckingWindowDto?> persisted) = ServiceCapturingCreate();

        CheckingWindowDto window = Window();
        window.Exercises =
        [
            Dated(CheckingExerciseType.ResultsEnquiry, new DateTime(2027, 1, 1), new DateTime(2027, 3, 1), 1)
        ];

        await service.CreateAsync(window, CancellationToken.None);

        Assert.Equal(
            CheckingExerciseType.ResultsEnquiry,
            Assert.Single(persisted()!.Exercises).ExerciseType);
    }

    // A caller that names no exercises at all predates the wizard and keeps the old shape.
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

    private static CheckingExerciseDto Dated(
        CheckingExerciseType type, DateTime start, DateTime end, int sortOrder) =>
        new() { ExerciseType = type, StartDate = start, EndDate = end, SortOrder = sortOrder };

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
