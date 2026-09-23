using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #466: one section per exercise, headed by its own name, with links keyed by id. A display-only
// exercise cannot validate (no store until slice 2) and cannot close (nothing to sweep).
public class SummaryControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PupilDataId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SummaryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static CheckingWindowDatasetDto Complete(string name) => new()
    {
        Name = name, IngressFile = $"{name}.csv", SchemaFile = $"{name}.json"
    };

    private static readonly Guid ResultsEnquiryId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId, Title = "16 to 19", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 6, 1),
        Exercises =
        [
            new CheckingExerciseDto { Id = SummaryId, ExerciseType = null, Name = "Summary data (Autumn)", TabName = "Summary",
                StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddMonths(2), SortOrder = 2,
                Datasets = [Complete("summary")] },
            new CheckingExerciseDto { Id = PupilDataId, ExerciseType = CheckingExerciseType.PupilData, Name = "Pupil data checking", TabName = "Pupils",
                StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(14), SortOrder = 0,
                Datasets = [Complete("included"), Complete("nonincluded")] },
            // No datasets at all — e.g. results enquiry on a window type with no results feed.
            // Close is still an admin decision independent of the dataset list (#466 review fix).
            new CheckingExerciseDto { Id = ResultsEnquiryId, ExerciseType = CheckingExerciseType.ResultsEnquiry, Name = "Results enquiry", TabName = "Results",
                StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(14), SortOrder = 1,
                Datasets = [] }
        ]
    };

    private static async Task<WindowEditItem> ModelFor(CheckingWindowDto window)
    {
        var service = Substitute.For<IWindowService>();
        service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);
        var view = Assert.IsType<ViewResult>(await new SummaryController(service).Index(WindowId, CancellationToken.None));
        return Assert.IsType<WindowEditItem>(view.Model);
    }

    [Fact]
    public async Task Sections_are_in_sort_order_and_headed_by_the_exercises_name()
    {
        var model = await ModelFor(Window());

        Assert.Equal(["Pupil data checking", "Results enquiry", "Summary data (Autumn)"], model.Exercises.Select(e => e.Label));
        Assert.Equal(["Pupils", "Results", "Summary"], model.Exercises.Select(e => e.TabName));
    }

    [Fact]
    public async Task Links_are_keyed_by_exercise_id()
    {
        var model = await ModelFor(Window());
        var section = model.Exercises.Single(e => e.ExerciseId == PupilDataId);

        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/edit", section.EditLink);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/remove", section.RemoveLink);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/validate", section.ValidateLink);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/close", section.CloseLink);
        var dataset = section.Datasets.First();
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/ingress-file/included", dataset.IngressFileLink);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/schema-file/included", dataset.SchemaFileLink);
    }

    [Fact]
    public async Task A_kind_exercise_with_files_and_live_dates_can_validate_and_close()
    {
        var model = await ModelFor(Window());
        var section = model.Exercises.Single(e => e.ExerciseId == PupilDataId);

        Assert.True(section.IsValidatable);
        Assert.Null(section.ValidateDisabledReason);
        Assert.True(section.CanClose);
    }

    [Fact]
    public async Task A_display_only_exercise_cannot_validate_and_says_why_and_cannot_close()
    {
        var model = await ModelFor(Window());
        var section = model.Exercises.Single(e => e.ExerciseId == SummaryId);

        Assert.False(section.IsValidatable);
        // Assert against the constant, not the literal, so a copy-edit of the message can't
        // silently desync the test from the string the view actually renders (#466 review fix).
        Assert.Equal(ExerciseSummarySection.NoStorageReason, section.ValidateDisabledReason);
        Assert.False(section.CanClose);
    }

    [Fact]
    public async Task A_kind_exercise_missing_files_cannot_validate_and_gives_no_storage_reason()
    {
        var window = Window();
        window.Exercises.Single(e => e.Id == PupilDataId).Datasets.Single(d => d.Name == "nonincluded").SchemaFile = "";
        var model = await ModelFor(window);
        var section = model.Exercises.Single(e => e.ExerciseId == PupilDataId);

        // The view falls back to a generic "not all files supplied" message for a kind exercise —
        // ValidateDisabledReason itself stays null, because that field is reserved for the
        // missing-storage case (#466 review fix).
        Assert.False(section.IsValidatable);
        Assert.Null(section.ValidateDisabledReason);
    }

    [Fact]
    public async Task Validatable_is_projected_straight_from_the_dtos_own_CanValidate()
    {
        // The page's gate must be the same gate ValidateWindowController runs behind, not a
        // second copy that can drift (#466 review fix): no extra date condition here.
        var window = Window();
        var pupilData = window.Exercises.Single(e => e.Id == PupilDataId);
        pupilData.StartDate = DateTime.UtcNow.Date.AddYears(-2);
        pupilData.EndDate = DateTime.UtcNow.Date.AddYears(-1); // end date long past
        var model = await ModelFor(window);
        var section = model.Exercises.Single(e => e.ExerciseId == PupilDataId);

        Assert.Equal(pupilData.CanValidate, section.IsValidatable);
        Assert.True(section.IsValidatable); // files are complete, so CanValidate is true regardless of dates
    }

    [Fact]
    public async Task A_stale_validation_stamp_is_reported_as_such()
    {
        var window = Window();
        var pupilData = window.Exercises.Single(e => e.Id == PupilDataId);
        pupilData.ValidatedAt = DateTime.UtcNow.AddDays(-1);
        pupilData.ValidatedIngressChecksum = "stale-checksum";
        var model = await ModelFor(window);
        var section = model.Exercises.Single(e => e.ExerciseId == PupilDataId);

        Assert.False(section.IsValidated);
        Assert.True(section.IsStale);
        Assert.NotNull(section.ValidatedAt);
    }

    [Fact]
    public async Task An_exercise_with_no_datasets_still_offers_close()
    {
        // Close sweeps journeys/drafts, not files, so a kind exercise with zero dataset rows still
        // gets a Close button (#466 review fix).
        var model = await ModelFor(Window());
        var section = model.Exercises.Single(e => e.ExerciseId == ResultsEnquiryId);

        Assert.Empty(section.Datasets);
        Assert.True(section.CanClose);
    }

    [Fact]
    public async Task The_add_exercise_link_points_at_the_window()
    {
        var model = await ModelFor(Window());

        Assert.Equal($"/admin/windows/{WindowId}/exercises/add", model.AddExerciseLink);
    }
}
