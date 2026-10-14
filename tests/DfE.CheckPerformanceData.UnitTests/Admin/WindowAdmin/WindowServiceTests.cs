using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
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

    // #535: 16:30 UTC on a summer day is 17:30 in the UK — half an hour after a 17:00 end.
    [Fact]
    public async Task GetAllDataAsync_decides_open_on_the_UK_clock()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.GetAllWindowsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CheckingWindowDto>
            {
                Window(new DateTime(2026, 7, 1), new DateTime(2026, 7, 15, 17, 0, 0)),
                Window(new DateTime(2026, 7, 1), new DateTime(2026, 7, 15, 17, 30, 0))
            });

        WindowService service = new(repository, new UkClockAt("2026-07-15T16:30:00Z"));

        PageResult? result = await service.GetAllDataAsync(CancellationToken.None);

        Assert.Equal(new[] { false, true }, result!.Windows.Select(w => w.IsOpen));
    }

    // #324: dataset slots are reconciled for every exercise, not just pupil data. Without this an
    // admin has nowhere to upload the results files, and the enquiry journey has nothing to read on
    // any environment SeedResults does not run on.
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
        Assert.Equal(7, enquiry.Datasets.Count);
        Assert.All(enquiry.Datasets, d => Assert.Equal(d.Name, d.SourceFile));

        // The pupil-data exercise gets no supplier slots: the admin adds its files.
        Assert.Empty(persisted.FindExercise(CheckingExerciseType.PupilData)!.Datasets);
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

    [Fact]
    public async Task CreateAsync_derives_the_key_stage_from_the_window_type()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        repository.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CheckingWindowDto>());
        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.CheckingWindowType = CheckingWindowType.Post16;

        await service.CreateAsync(window, CancellationToken.None);

        await repository.Received(1).CreateAsync(
            Arg.Is<CheckingWindowDto>(w => w.KeyStage == KeyStages.Post16),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_moves_the_key_stage_when_the_window_type_changes()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        WindowService service = new(repository, TimeProvider.System);

        // Stored as KS2 / KS2; the admin changes the type on the summary page.
        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.CheckingWindowType = CheckingWindowType.KS4Autumn;

        await service.UpdateAsync(window, CancellationToken.None);

        await repository.Received(1).UpdateAsync(
            Arg.Is<CheckingWindowDto>(w => w.KeyStage == KeyStages.KS4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saving_an_exercise_with_no_tab_name_is_refused()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        WindowService service = new(repository, TimeProvider.System);

        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        CheckingExerciseDto exercise = Exercise(CheckingExerciseType.PupilData);
        exercise.TabName = " ";
        window.Exercises = [exercise];

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(window, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(window, CancellationToken.None));
        await repository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task A_save_that_adds_a_second_exercise_of_a_kind_is_refused_even_when_it_is_disabled()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        WindowService service = new(repository, TimeProvider.System);

        CheckingExerciseDto existing = Enabled(Exercise(CheckingExerciseType.PupilData));
        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.Exercises = [existing, Exercise(CheckingExerciseType.PupilData)];
        Stored(repository, window, Same(existing));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(window, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(window, CancellationToken.None));
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task A_window_that_already_holds_two_of_a_kind_can_still_be_saved()
    {
        // Saved before the one-per-kind rule. Refusing every save would leave the admin no way to
        // fix it but deleting one.
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        WindowService service = new(repository, TimeProvider.System);

        CheckingExerciseDto first = Enabled(Exercise(CheckingExerciseType.PupilData));
        CheckingExerciseDto second = Enabled(Exercise(CheckingExerciseType.PupilData));
        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.Exercises = [first, second];
        Stored(repository, window, Same(first), Same(second));

        await service.UpdateAsync(window, CancellationToken.None);

        await repository.Received(1).UpdateAsync(window, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(KindChangeBlock.LegacyStorage)]
    [InlineData(KindChangeBlock.LiveRelease)]
    [InlineData(KindChangeBlock.ChangeRequests)]
    public async Task A_blocked_kind_change_is_refused(KindChangeBlock block)
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        WindowService service = new(repository, TimeProvider.System);

        CheckingExerciseDto before = new()
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData, TabName = "Tab",
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 2, 1),
            UsesExerciseStorage = block != KindChangeBlock.LegacyStorage,
            CurrentReleaseId = block == KindChangeBlock.LiveRelease ? Guid.NewGuid() : null
        };
        repository.HasChangeRequestsAsync(before.Id, Arg.Any<CancellationToken>())
            .Returns(block == KindChangeBlock.ChangeRequests);
        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.Exercises = [Retyped(before, CheckingExerciseType.ResultsEnquiry)];
        Stored(repository, window, before);

        Assert.Equal(block, await service.KindChangeBlockAsync(before, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(window, CancellationToken.None));
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task A_kind_change_with_nothing_behind_it_is_allowed()
    {
        IWindowRepository repository = Substitute.For<IWindowRepository>();
        WindowService service = new(repository, TimeProvider.System);

        CheckingExerciseDto before = new()
        {
            Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData, TabName = "Tab",
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 2, 1), UsesExerciseStorage = true
        };
        CheckingWindowDto window = Window(new DateTime(2027, 1, 1), new DateTime(2027, 2, 1));
        window.Exercises = [Retyped(before, CheckingExerciseType.ResultsEnquiry)];
        Stored(repository, window, before);

        Assert.Equal(KindChangeBlock.None, await service.KindChangeBlockAsync(before, CancellationToken.None));
        await service.UpdateAsync(window, CancellationToken.None);

        await repository.Received(1).UpdateAsync(window, Arg.Any<CancellationToken>());
    }

    // The stored copy of the window the save replaces, as the repository returns it.
    private static void Stored(IWindowRepository repository, CheckingWindowDto window,
        params CheckingExerciseDto[] exercises) =>
        repository.GetByIdAsync(window.Id, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = window.Id, Title = window.Title, StartDate = window.StartDate, EndDate = window.EndDate,
            KeyStage = window.KeyStage, CheckingWindowType = window.CheckingWindowType, Exercises = [.. exercises]
        });

    private static CheckingExerciseDto Same(CheckingExerciseDto e) => Retyped(e, e.ExerciseType);

    private static CheckingExerciseDto Retyped(CheckingExerciseDto e, CheckingExerciseType? type) => new()
    {
        Id = e.Id, ExerciseType = type, TabName = e.TabName, IsEnabled = e.IsEnabled,
        UsesExerciseStorage = e.UsesExerciseStorage, StartDate = e.StartDate, EndDate = e.EndDate,
        TabOrder = e.TabOrder
    };

    private static CheckingExerciseDto Enabled(CheckingExerciseDto e) => new()
    {
        Id = Guid.NewGuid(), ExerciseType = e.ExerciseType, TabName = e.TabName, IsEnabled = true,
        StartDate = e.StartDate, EndDate = e.EndDate, TabOrder = e.TabOrder
    };

    private static CheckingExerciseDto Exercise(CheckingExerciseType type) =>
        new()
        {
            ExerciseType = type,
            TabName = "Tab",
            StartDate = new DateTime(2027, 1, 1),
            EndDate = new DateTime(2027, 2, 1),
            TabOrder = WindowExercises.DefaultTabOrder(type)
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
