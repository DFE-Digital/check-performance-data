using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class WindowServiceTests
{
    [Fact]
    public async Task CreateAsync_persists_the_supplied_start_and_end_times_unchanged()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CheckingWindowDto>());

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 9, 30, 0),
            endDate: new DateTime(2027, 2, 1, 22, 15, 0));

        await service.CreateAsync(window, CancellationToken.None);

        await repository.Received(1).CreateAsync(
            Arg.Is<CheckingWindowDto>(w =>
                w.StartDate == new DateTime(2027, 1, 1, 9, 30, 0) &&
                w.EndDate == new DateTime(2027, 2, 1, 22, 15, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_persists_the_supplied_start_and_end_times_unchanged()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 8, 0, 0),
            endDate: new DateTime(2027, 2, 1, 16, 45, 0));

        await service.UpdateAsync(window, CancellationToken.None);

        await repository.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w =>
                w.StartDate == new DateTime(2027, 1, 1, 8, 0, 0) &&
                w.EndDate == new DateTime(2027, 2, 1, 16, 45, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_keeps_dates_unspecified_for_the_timestamp_without_time_zone_column()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        CheckingWindowDto? persisted = null;
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                persisted = call.Arg<CheckingWindowDto>();
                return persisted;
            });

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 9, 30, 15, DateTimeKind.Unspecified),
            endDate: new DateTime(2027, 2, 1, 9, 30, 15, DateTimeKind.Unspecified));

        await service.CreateAsync(window, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(DateTimeKind.Unspecified, persisted!.StartDate.Kind);
        Assert.Equal(DateTimeKind.Unspecified, persisted.EndDate.Kind);
    }

    [Fact]
    public async Task CreateAsync_leaves_already_normalised_dates_unchanged()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CheckingWindowDto>());

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(
            startDate: new DateTime(2027, 1, 1, 0, 0, 0),
            endDate: new DateTime(2027, 2, 1, 17, 0, 0));

        await service.CreateAsync(window, CancellationToken.None);

        await repository.Received(1).CreateAsync(
            Arg.Is<CheckingWindowDto>(w =>
                w.StartDate == new DateTime(2027, 1, 1, 0, 0, 0) &&
                w.EndDate == new DateTime(2027, 2, 1, 17, 0, 0)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllDataAsync_returns_every_window_from_the_repository()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.GetAllWindowsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>
            {
                Window(new DateTime(2020, 1, 1), new DateTime(2020, 2, 1)),
                Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1))
            });

        WindowService service = new(repository, new StubTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero)));

        PageResult? result = await service.GetAllDataAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Windows.Count);
    }

    [Fact]
    public async Task GetAllDataAsync_marks_a_window_open_when_now_is_within_its_dates()
    {
        DateTimeOffset now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.GetAllWindowsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>
            {
                Window(now.DateTime.AddDays(-1), now.DateTime.AddDays(1))
            });

        WindowService service = new(repository, new StubTimeProvider(now));

        PageResult? result = await service.GetAllDataAsync(CancellationToken.None);

        Assert.True(result!.Windows.Single().IsOpen);
    }

    [Theory]
    [InlineData(-10, -5)] // already ended
    [InlineData(5, 10)]   // not yet started
    public async Task GetAllDataAsync_marks_a_window_closed_when_now_is_outside_its_dates(int startOffsetDays, int endOffsetDays)
    {
        DateTimeOffset now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.GetAllWindowsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>
            {
                Window(now.DateTime.AddDays(startOffsetDays), now.DateTime.AddDays(endOffsetDays))
            });

        WindowService service = new(repository, new StubTimeProvider(now));

        PageResult? result = await service.GetAllDataAsync(CancellationToken.None);

        Assert.False(result!.Windows.Single().IsOpen);
    }

    // #324: dataset slots are reconciled for every exercise, not just pupil data. Without this an
    // admin has nowhere to upload the results files, and the enquiry journey has nothing to read on
    // any environment SeedStudentResults does not run on.
    [Fact]
    public async Task UpdateAsync_gives_a_results_enquiry_exercise_a_slot_per_source_file()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        CheckingWindowDto? persisted = null;
        _ = repository.UpdateAsync(Arg.Do<CheckingWindowDto>(w => persisted = w), Arg.Any<CancellationToken>());

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.CheckingWindowType = CheckingWindowType.Post16;
        window.Exercises =
        [
            Exercise(CheckingExerciseType.PupilData),
            Exercise(CheckingExerciseType.ResultsEnquiry)
        ];

        await service.UpdateAsync(window, CancellationToken.None);

        CheckingExerciseDto enquiry = persisted!.FindExercise(CheckingExerciseType.ResultsEnquiry)!;
        Assert.Equal(5, enquiry.Datasets.Count);
        Assert.All(enquiry.Datasets, d => Assert.Equal(d.Name, d.SourceFile));

        // The pupil-data exercise keeps its own slots, unaffected.
        Assert.Equal(
            ["included", "nonincluded"],
            persisted.FindExercise(CheckingExerciseType.PupilData)!.Datasets.Select(d => d.Name));
    }

    [Fact]
    public async Task UpdateAsync_keeps_a_results_file_that_has_already_been_uploaded()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        CheckingWindowDto? persisted = null;
        _ = repository.UpdateAsync(Arg.Do<CheckingWindowDto>(w => persisted = w), Arg.Any<CancellationToken>());

        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.CheckingWindowType = CheckingWindowType.Post16;
        CheckingExerciseDto enquiry = Exercise(CheckingExerciseType.ResultsEnquiry);
        enquiry.Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = ResultsFileTags.Post16Main,
                SourceFile = ResultsFileTags.Post16Main,
                IngressFile = "main.csv",
                IngressFileChecksum = "ABC",
                SortOrder = 0
            }
        ];
        window.Exercises = [enquiry];

        await service.UpdateAsync(window, CancellationToken.None);

        CheckingWindowDatasetDto main = persisted!
            .FindExercise(CheckingExerciseType.ResultsEnquiry)!
            .Datasets.Single(d => d.Name == ResultsFileTags.Post16Main);
        Assert.Equal("main.csv", main.IngressFile);
        Assert.Equal("ABC", main.IngressFileChecksum);
    }

    private static CheckingExerciseDto Exercise(CheckingExerciseType type) =>
        new()
        {
            ExerciseType = type,
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 2, 1),
            SortOrder = WindowExercises.SortOrderFor(type)
        };

    private static CheckingWindowDto Window(DateTime startDate, DateTime endDate) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "Existing window",
            StartDate = startDate,
            EndDate = endDate,
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2
        };

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
