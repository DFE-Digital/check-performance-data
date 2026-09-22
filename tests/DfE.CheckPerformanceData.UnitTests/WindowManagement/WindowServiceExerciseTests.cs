using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// #466 slice 1: the rules for adding, editing and removing an exercise live here, never in a
// controller. Every write goes through IWindowRepository.UpdateAsync with the whole window.
public class WindowServiceExerciseTests
{
    private static readonly Guid WindowId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PupilDataId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SummaryId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly IWindowRepository _repository = Substitute.For<IWindowRepository>();
    private CheckingWindowDto? _persisted;

    private WindowService Build(CheckingWindowDto window)
    {
        _repository.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);
        _repository.UpdateAsync(Arg.Do<CheckingWindowDto>(w => _persisted = w), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return new WindowService(_repository, TimeProvider.System);
    }

    private static CheckingWindowDto Post16Window() => new()
    {
        Id = WindowId,
        Title = "16 to 19",
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2026, 10, 7),
        EndDate = new DateTime(2027, 3, 31),
        Exercises =
        [
            new CheckingExerciseDto
            {
                Id = PupilDataId, ExerciseType = CheckingExerciseType.PupilData,
                Name = "Pupil data checking", TabName = "Pupils",
                StartDate = new DateTime(2026, 10, 7), EndDate = new DateTime(2026, 10, 18), SortOrder = 0,
                Datasets = [new CheckingWindowDatasetDto { Name = "included", Included = true },
                            new CheckingWindowDatasetDto { Name = "nonincluded", Included = false, SortOrder = 1 }]
            },
            new CheckingExerciseDto
            {
                Id = SummaryId, ExerciseType = null,
                Name = "Summary data (Autumn)", TabName = "Summary",
                StartDate = new DateTime(2026, 10, 7), EndDate = new DateTime(2026, 11, 30), SortOrder = 2,
                Datasets = [new CheckingWindowDatasetDto { Name = "summary", IngressFile = "summary.csv" }]
            }
        ]
    };

    private static ExerciseDefinition Definition(
        string name = "Retention", string tabName = "Retention", int sortOrder = 5,
        DateTime? start = null, DateTime? end = null) =>
        new(name, tabName, sortOrder, start ?? new DateTime(2027, 3, 1), end ?? new DateTime(2027, 3, 31));

    // ---- Add ----

    [Fact]
    public async Task Add_creates_a_display_only_exercise_with_one_required_data_slot()
    {
        var service = Build(Post16Window());

        var result = await service.AddExerciseAsync(WindowId, Definition(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var added = _persisted!.Exercises.Single(e => e.Name == "Retention");
        Assert.Null(added.ExerciseType);
        Assert.Equal("Retention", added.TabName);
        Assert.Equal(5, added.SortOrder);
        var slot = Assert.Single(added.Datasets);
        Assert.Equal("data", slot.Name);
        Assert.True(slot.Required);
    }

    [Fact]
    public async Task Add_widens_the_windows_dates_to_include_the_new_exercise()
    {
        var service = Build(Post16Window());

        await service.AddExerciseAsync(WindowId,
            Definition(start: new DateTime(2027, 3, 1), end: new DateTime(2027, 4, 30)), CancellationToken.None);

        Assert.Equal(new DateTime(2027, 4, 30), _persisted!.EndDate);
    }

    [Theory]
    [InlineData("", "Retention", "Enter a name")]
    [InlineData("   ", "Retention", "Enter a name")]
    [InlineData("Retention", "", "Enter a tab name")]
    public async Task Add_refuses_a_blank_name_or_tab_name(string name, string tabName, string reason)
    {
        var service = Build(Post16Window());

        var result = await service.AddExerciseAsync(WindowId, Definition(name, tabName), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(reason, result.Reason);
        Assert.Null(_persisted);
    }

    [Theory]
    [InlineData("Pupil data checking")]
    [InlineData("pupil DATA checking")]
    [InlineData("  Summary data (Autumn)  ")]
    public async Task Add_refuses_a_name_already_used_in_the_window_ignoring_case_and_whitespace(string name)
    {
        var service = Build(Post16Window());

        var result = await service.AddExerciseAsync(WindowId, Definition(name), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("An exercise with that name already exists in this window", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task Add_refuses_an_end_before_the_start()
    {
        var service = Build(Post16Window());

        var result = await service.AddExerciseAsync(WindowId,
            Definition(start: new DateTime(2027, 3, 31), end: new DateTime(2027, 3, 1)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("End date can not occur before the start date", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task Add_refuses_a_name_over_the_column_length()
    {
        var service = Build(Post16Window());

        var result = await service.AddExerciseAsync(WindowId,
            Definition(new string('a', ExerciseDefinition.MaxNameLength + 1)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Name must be 200 characters or less", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task Add_refuses_a_tab_name_over_the_column_length()
    {
        var service = Build(Post16Window());

        var result = await service.AddExerciseAsync(WindowId,
            Definition(tabName: new string('a', ExerciseDefinition.MaxTabNameLength + 1)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Tab name must be 100 characters or less", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task Add_trims_the_name_and_tab_name()
    {
        var service = Build(Post16Window());

        await service.AddExerciseAsync(WindowId, Definition("  Retention ", " Ret "), CancellationToken.None);

        var added = _persisted!.Exercises.Single(e => e.SortOrder == 5);
        Assert.Equal("Retention", added.Name);
        Assert.Equal("Ret", added.TabName);
    }

    [Fact]
    public async Task Add_fails_for_an_unknown_window()
    {
        _repository.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);
        var service = new WindowService(_repository, TimeProvider.System);

        var result = await service.AddExerciseAsync(WindowId, Definition(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Window not found", result.Reason);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_changes_name_tab_name_order_and_dates_but_keeps_kind_and_slots()
    {
        var service = Build(Post16Window());

        var result = await service.UpdateExerciseAsync(WindowId, PupilDataId,
            Definition("Student data checking", "Students", 9,
                new DateTime(2026, 10, 8), new DateTime(2026, 10, 20)), CancellationToken.None);

        Assert.True(result.Succeeded);
        var updated = _persisted!.Exercises.Single(e => e.Id == PupilDataId);
        Assert.Equal(CheckingExerciseType.PupilData, updated.ExerciseType);
        Assert.Equal("Student data checking", updated.Name);
        Assert.Equal("Students", updated.TabName);
        Assert.Equal(9, updated.SortOrder);
        Assert.Equal(new DateTime(2026, 10, 8), updated.StartDate);
        Assert.Equal(new DateTime(2026, 10, 20), updated.EndDate);
        Assert.Equal(["included", "nonincluded"], updated.Datasets.Select(d => d.Name));
    }

    [Fact]
    public async Task Update_keeps_a_display_only_exercises_uploaded_files()
    {
        var service = Build(Post16Window());

        await service.UpdateExerciseAsync(WindowId, SummaryId, Definition("Summary (Autumn)", "Summary", 2),
            CancellationToken.None);

        var updated = _persisted!.Exercises.Single(e => e.Id == SummaryId);
        Assert.Equal("summary.csv", Assert.Single(updated.Datasets).IngressFile);
    }

    [Fact]
    public async Task Update_allows_the_exercise_to_keep_its_own_name()
    {
        var service = Build(Post16Window());

        var result = await service.UpdateExerciseAsync(WindowId, SummaryId,
            Definition("Summary data (Autumn)", "Summary", 2), CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Update_refuses_another_exercises_name()
    {
        var service = Build(Post16Window());

        var result = await service.UpdateExerciseAsync(WindowId, SummaryId,
            Definition("Pupil data checking", "Summary", 2), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("An exercise with that name already exists in this window", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task Update_fails_for_an_unknown_exercise()
    {
        var service = Build(Post16Window());

        var result = await service.UpdateExerciseAsync(WindowId, Guid.NewGuid(), Definition(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Exercise not found", result.Reason);
        Assert.Null(_persisted);
    }

    // ---- Remove ----

    [Fact]
    public async Task Remove_drops_the_exercise_and_rederives_the_windows_dates()
    {
        var window = Post16Window();
        window.Exercises[1].EndDate = new DateTime(2027, 6, 30);
        var service = Build(window);

        var result = await service.RemoveExerciseAsync(WindowId, SummaryId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(_persisted!.Exercises, e => e.Id == SummaryId);
        Assert.Equal(new DateTime(2026, 10, 18), _persisted.EndDate);
    }

    [Fact]
    public async Task Remove_is_refused_when_the_exercise_holds_change_requests()
    {
        var service = Build(Post16Window());
        _repository.HasChangeRequestsAsync(PupilDataId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await service.RemoveExerciseAsync(WindowId, PupilDataId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("This exercise has change requests and cannot be removed", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task Remove_is_refused_for_the_windows_last_exercise()
    {
        var window = Post16Window();
        window.Exercises.RemoveAt(1);
        var service = Build(window);

        var result = await service.RemoveExerciseAsync(WindowId, PupilDataId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("A window must keep at least one exercise", result.Reason);
        Assert.Null(_persisted);
        // The last-exercise guard must run before the change-requests check, so a window that
        // cannot lose its last exercise never pays for the lookup.
        await _repository.DidNotReceive().HasChangeRequestsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_fails_for_an_unknown_exercise()
    {
        var service = Build(Post16Window());

        var result = await service.RemoveExerciseAsync(WindowId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Exercise not found", result.Reason);
        Assert.Null(_persisted);
    }

    // ---- Names on create/update ----

    [Fact]
    public async Task A_kind_exercise_with_no_name_is_given_its_default_on_update()
    {
        var window = Post16Window();
        window.Exercises[0].Name = "";
        window.Exercises[0].TabName = "";
        var service = Build(window);

        await service.UpdateAsync(window, CancellationToken.None);

        Assert.Equal("Pupil data checking", _persisted!.Exercises[0].Name);
        Assert.Equal("Pupils", _persisted.Exercises[0].TabName);
    }

    [Fact]
    public async Task A_display_only_exercises_slots_survive_an_update()
    {
        // EnsureDatasetsMatchType used to replace every exercise's slots with the type's defaults;
        // a display-only exercise has no type defaults, so its slots would have been wiped.
        var window = Post16Window();
        var service = Build(window);

        await service.UpdateAsync(window, CancellationToken.None);

        Assert.Equal("summary", Assert.Single(_persisted!.Exercises[1].Datasets).Name);
    }

    // ---- SetExercises ----

    [Fact]
    public async Task SetExercises_keeps_existing_and_adds_a_newly_selected_template_on_the_windows_dates()
    {
        var window = Post16Window();
        var service = Build(window);

        var result = await service.SetExercisesAsync(WindowId,
            ["Pupil data checking", "Results enquiry"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["Pupil data checking", "Results enquiry"], _persisted!.Exercises.Select(e => e.Name));
        var added = _persisted.Exercises.Single(e => e.Name == "Results enquiry");
        Assert.Equal(CheckingExerciseType.ResultsEnquiry, added.ExerciseType);
        Assert.Equal(window.StartDate, added.StartDate);
        Assert.Equal(window.EndDate, added.EndDate);
        Assert.NotEmpty(added.Datasets);
    }

    [Fact]
    public async Task SetExercises_removes_an_unticked_exercise_when_it_holds_no_change_requests()
    {
        var service = Build(Post16Window());

        var result = await service.SetExercisesAsync(WindowId, ["Pupil data checking"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(_persisted!.Exercises, e => e.Id == SummaryId);
    }

    [Fact]
    public async Task SetExercises_refuses_when_an_unticked_exercise_holds_change_requests_and_persists_nothing()
    {
        var service = Build(Post16Window());
        _repository.HasChangeRequestsAsync(SummaryId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await service.SetExercisesAsync(WindowId, ["Pupil data checking"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Summary data (Autumn) has change requests and cannot be removed", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task SetExercises_refuses_when_nothing_selected_matches_an_existing_or_template_name()
    {
        var service = Build(Post16Window());

        var result = await service.SetExercisesAsync(WindowId, ["Nonexistent"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Select at least one checking exercise", result.Reason);
        Assert.Null(_persisted);
    }

    [Fact]
    public async Task SetExercises_matches_names_case_insensitively()
    {
        var service = Build(Post16Window());

        var result = await service.SetExercisesAsync(WindowId, ["PUPIL DATA CHECKING"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Pupil data checking", Assert.Single(_persisted!.Exercises).Name);
    }
}
