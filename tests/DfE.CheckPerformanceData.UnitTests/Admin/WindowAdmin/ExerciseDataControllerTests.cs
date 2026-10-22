using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public sealed class ExerciseDataControllerTests
{
    private readonly IWindowService _service = Substitute.For<IWindowService>();
    private readonly CheckingWindowDto _window = CreateCheckingExerciseControllerTests.Window();
    private readonly ExerciseDataController _controller;

    public ExerciseDataControllerTests()
    {
        _service.GetByIdAsync(_window.Id, Arg.Any<CancellationToken>()).Returns(_window);
        var urls = Substitute.For<IUrlHelper>();
        urls.Action(Arg.Any<UrlActionContext>()).Returns("/exercise");
        _controller = new(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }, Url = urls
        };
    }

    [Fact]
    public async Task Add_creates_one_pair_only_on_the_selected_exercise()
    {
        var first = _window.Exercises[0];
        first.Datasets.Add(new() { Id = Guid.NewGuid(), Name = "existing", SortOrder = 3, SchemaFile = "schema.json" });
        first.ValidatedAt = DateTime.Now;
        var second = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = first.ExerciseType, StartDate = first.StartDate, EndDate = first.EndDate
        };
        _window.Exercises.Add(second);
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "extra-pupils", Inclusion = "included", Required = false };
        var result = Assert.IsType<RedirectToActionResult>(await _controller.Submit(_window.Id, first.Id, model, default));
        var added = Assert.Single(first.Datasets, d => d.Name == model.Name);
        Assert.Equal(4, added.SortOrder);
        Assert.True(added.Included);
        Assert.False(added.Required);
        // On pupil data checking every file is merged into the pupils data the journey reads.
        Assert.Equal(CheckingExerciseType.PupilData, first.ExerciseType);
        Assert.True(added.FeedsJourney);
        Assert.Empty(added.IngressFile);
        Assert.Empty(added.SchemaFile);
        Assert.Null(first.ValidatedAt);
        Assert.Empty(second.Datasets);
        Assert.Equal("EditCheckingExercise", result.ControllerName);
        Assert.Equal(first.Id, result.RouteValues!["exerciseId"]);
        // The tabs component opens the tab named by the fragment.
        Assert.Equal(ExerciseLinks.DataTab, result.Fragment);
        await _service.Received(1).UpdateAsync(_window, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Data_actions_require_admin_access_and_posts_require_antiforgery()
    {
        foreach (var controller in new[] { typeof(ExerciseDataController), typeof(IngressFileController), typeof(SchemaController), typeof(ValidateWindowController) })
            Assert.NotNull(Attribute.GetCustomAttribute(controller, typeof(DfE.CheckPerformanceData.Web.Admin.RequireAdminSectionAttribute)));
        foreach (var (controller, action) in new[]
        {
            (typeof(ExerciseDataController), "Submit"), (typeof(ExerciseDataController), "Retire"),
            (typeof(ExerciseDataController), "PutBackInUse"), (typeof(IngressFileController), "Select"),
            (typeof(SchemaController), "Submit"), (typeof(ValidateWindowController), "Validate")
        })
            Assert.NotNull(Attribute.GetCustomAttribute(controller.GetMethod(action)!, typeof(ValidateAntiForgeryTokenAttribute)));
    }

    [Fact]
    public async Task Duplicate_names_do_not_save()
    {
        // The name is the source stamped on a results file's rows, so it must be unique.
        var owner = AddExercise(CheckingExerciseType.ResultsEnquiry);
        owner.Datasets.Add(new() { Name = "existing" });
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "existing" };
        var result = Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, owner.Id, model, default));
        Assert.Same(model, result.Model);
        Assert.Contains("Name", _controller.ModelState.Keys);
        Assert.Equal("/exercise", model.CancelUrl);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    // #466 carried over from Task 17: the duplicate-name guard is scoped to one exercise, which is
    // the property that justifies routing schema/ingress uploads by exercise id rather than kind —
    // two exercises can each hold a slot with the same name.
    [Fact]
    public async Task Same_name_is_allowed_on_a_different_exercise()
    {
        var first = _window.Exercises[0];
        first.Datasets.Add(new() { Name = "main" });
        var second = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = first.ExerciseType, StartDate = first.StartDate, EndDate = first.EndDate
        };
        _window.Exercises.Add(second);

        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "main" };
        var result = await _controller.Submit(_window.Id, second.Id, model, default);

        Assert.IsType<RedirectToActionResult>(result);
        var added = Assert.Single(second.Datasets, d => d.Name == "main");
        Assert.Equal(0, added.SortOrder);
        await _service.Received(1).UpdateAsync(_window, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("../file")]
    [InlineData("file/name")]
    [InlineData(@"file\name")]
    [InlineData("..")]
    [InlineData(" . ")]
    public async Task Invalid_names_redisplay_the_form(string? name)
    {
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = name };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, true);
        foreach (var error in errors)
            foreach (var member in error.MemberNames)
                _controller.ModelState.AddModelError(member, error.ErrorMessage!);
        Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, _window.Exercises[0].Id, model, default));
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Missing_or_foreign_exercise_and_parent_mismatch_are_rejected()
    {
        Assert.IsType<NotFoundResult>(await _controller.New(_window.Id, Guid.NewGuid(), default));
        Assert.IsType<NotFoundResult>(await _controller.New(Guid.NewGuid(), _window.Exercises[0].Id, default));
        Assert.IsType<NotFoundResult>(await _controller.Submit(_window.Id, Guid.NewGuid(), new() { WindowId = _window.Id }, default));
        Assert.IsType<BadRequestResult>(await _controller.Submit(_window.Id, _window.Exercises[0].Id, new() { WindowId = Guid.NewGuid() }, default));
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task A_pupil_data_file_added_as_a_data_share_does_not_feed_the_journey_and_has_no_inclusion()
    {
        // e.g. the 16-19 previously published file: shown on the tab, never merged into the
        // pupils data the journeys read.
        var exercise = AddExercise(CheckingExerciseType.PupilData);
        var model = new AddExerciseDataItem
            { WindowId = _window.Id, Name = "Previously published", Use = "share", Inclusion = "included" };

        Assert.IsType<RedirectToActionResult>(await _controller.Submit(_window.Id, exercise.Id, model, default));

        var added = Assert.Single(exercise.Datasets);
        Assert.False(added.FeedsJourney);
        Assert.Null(added.Included);
    }

    [Fact]
    public async Task A_share_choice_posted_to_a_data_share_exercise_changes_nothing()
    {
        var exercise = AddExercise(null);
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "File", Use = "journey" };

        await _controller.Submit(_window.Id, exercise.Id, model, default);

        var added = Assert.Single(exercise.Datasets);
        Assert.False(added.FeedsJourney);
        Assert.Null(added.SourceFile);
    }

    [Fact]
    public void The_use_must_be_journey_or_share()
    {
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "File", Use = "other" };
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), [], true));
    }

    [Fact]
    public async Task A_results_file_added_as_a_data_share_is_display_only_and_stamps_no_source()
    {
        var exercise = AddExercise(CheckingExerciseType.ResultsEnquiry);
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "extra-share", Use = "share" };

        await _controller.Submit(_window.Id, exercise.Id, model, default);

        var added = Assert.Single(exercise.Datasets);
        Assert.False(added.FeedsJourney);
        Assert.Null(added.SourceFile);
    }

    [Fact]
    public async Task A_results_file_for_the_enquiry_feeds_the_journey_and_stamps_its_own_name()
    {
        // A supplier file nobody has seen before must reach the enquiry search with no code
        // change: its rows are stamped with the name the admin gave it, and schools see that name.
        var exercise = AddExercise(CheckingExerciseType.ResultsEnquiry);
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = " Potatoes ", Use = "journey", Required = false };

        Assert.IsType<RedirectToActionResult>(await _controller.Submit(_window.Id, exercise.Id, model, default));

        var added = Assert.Single(exercise.Datasets);
        Assert.True(added.FeedsJourney);
        Assert.Equal("Potatoes", added.SourceFile);
        Assert.Equal("Potatoes", ResultsSources.LabelFor(added.SourceFile));
    }

    [Fact]
    public async Task Retire_takes_a_slot_out_of_use_and_put_back_restores_it()
    {
        var exercise = AddExercise(CheckingExerciseType.ResultsEnquiry);
        var slot = new CheckingWindowDatasetDto { Id = Guid.NewGuid(), Name = ResultsFileTags.Post16LateResults1 };
        exercise.Datasets.Add(slot);

        var retired = Assert.IsType<RedirectToActionResult>(await _controller.Retire(_window.Id, exercise.Id, slot.Id, default));
        Assert.True(slot.Retired);
        Assert.Equal("EditCheckingExercise", retired.ControllerName);
        Assert.Equal(ExerciseLinks.DataTab, retired.Fragment);

        Assert.IsType<RedirectToActionResult>(await _controller.PutBackInUse(_window.Id, exercise.Id, slot.Id, default));
        Assert.False(slot.Retired);
        await _service.Received(2).UpdateAsync(_window, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retire_of_an_unknown_slot_or_exercise_is_not_found()
    {
        var exercise = AddExercise(CheckingExerciseType.ResultsEnquiry);
        var slot = new CheckingWindowDatasetDto { Id = Guid.NewGuid(), Name = "late" };
        exercise.Datasets.Add(slot);

        Assert.IsType<NotFoundResult>(await _controller.Retire(_window.Id, exercise.Id, Guid.NewGuid(), default));
        // A slot of another exercise cannot be retired through this one.
        Assert.IsType<NotFoundResult>(await _controller.Retire(_window.Id, _window.Exercises[0].Id, slot.Id, default));
        Assert.IsType<NotFoundResult>(await _controller.PutBackInUse(Guid.NewGuid(), exercise.Id, slot.Id, default));
        Assert.False(slot.Retired);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    // The name is not part of any blob path, so an admin may write it as they would say it.
    [Theory]
    [InlineData("File Name")]
    [InlineData("Late results (Jan)")]
    [InlineData("included-pupils")]
    [InlineData("KS4 v2.1")]
    public void Plain_text_names_are_valid(string name)
    {
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = name };
        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), [], true));
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, true, true)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, false, true)]
    [InlineData(null, false, false)]
    public async Task Each_question_is_asked_only_on_the_exercise_it_applies_to(
        CheckingExerciseType? type, bool asksInclusion, bool asksUse)
    {
        var exercise = AddExercise(type);

        var page = Assert.IsType<AddExerciseDataItem>(
            Assert.IsType<ViewResult>(await _controller.New(_window.Id, exercise.Id, default)).Model);

        Assert.Equal(asksInclusion, page.AsksInclusion);
        Assert.Equal(asksUse, page.AsksUse);
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, true)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, false)]
    [InlineData(null, false)]
    public async Task A_value_for_a_question_not_asked_is_not_stored(CheckingExerciseType? type, bool storesInclusion)
    {
        var exercise = AddExercise(type);
        // A hand-made post can send it.
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "File", Inclusion = "included" };

        Assert.IsType<RedirectToActionResult>(await _controller.Submit(_window.Id, exercise.Id, model, default));

        Assert.Equal(storesInclusion ? true : null, Assert.Single(exercise.Datasets).Included);
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData)]
    [InlineData(CheckingExerciseType.ResultsEnquiry)]
    public async Task A_journey_file_beside_another_journey_file_needs_confirmation(CheckingExerciseType type)
    {
        var exercise = AddExercise(type);
        exercise.Datasets.Add(new() { Id = Guid.NewGuid(), Name = "Existing", FeedsJourney = true });
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "Another", Use = "journey" };

        var view = Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, exercise.Id, model, default));

        Assert.True(_controller.ModelState.ContainsKey(nameof(AddExerciseDataItem.ConfirmJourney)));
        Assert.Equal(["Existing"], Assert.IsType<AddExerciseDataItem>(view.Model).ExistingJourneyFiles);
        Assert.Single(exercise.Datasets);
        await _service.DidNotReceive().UpdateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData)]
    [InlineData(CheckingExerciseType.ResultsEnquiry)]
    public async Task A_confirmed_journey_file_beside_another_journey_file_is_added(CheckingExerciseType type)
    {
        var exercise = AddExercise(type);
        exercise.Datasets.Add(new() { Id = Guid.NewGuid(), Name = "Existing", FeedsJourney = true });
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "Another", Use = "journey", ConfirmJourney = true };

        Assert.IsType<RedirectToActionResult>(await _controller.Submit(_window.Id, exercise.Id, model, default));

        Assert.True(Assert.Single(exercise.Datasets, d => d.Name == "Another").FeedsJourney);
    }

    [Theory]
    // A data share is not merged with anything, so there is nothing to confirm.
    [InlineData("share", true, false)]
    // A retired file is read by no run, so the new file is not merged with it.
    [InlineData("journey", true, true)]
    // A file that is itself a data share is not journey data.
    [InlineData("journey", false, false)]
    public async Task No_confirmation_is_needed_when_nothing_in_the_journey_is_merged_with(
        string use, bool existingFeedsJourney, bool existingRetired)
    {
        var exercise = AddExercise(CheckingExerciseType.PupilData);
        exercise.Datasets.Add(new()
            { Id = Guid.NewGuid(), Name = "Existing", FeedsJourney = existingFeedsJourney, Retired = existingRetired });
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "Another", Use = use };

        Assert.IsType<RedirectToActionResult>(await _controller.Submit(_window.Id, exercise.Id, model, default));
        Assert.Equal(2, exercise.Datasets.Count);
    }

    [Fact]
    public async Task The_page_names_the_journey_files_in_use()
    {
        var exercise = AddExercise(CheckingExerciseType.ResultsEnquiry);
        exercise.Datasets.Add(new() { Id = Guid.NewGuid(), Name = "Second", FeedsJourney = true, SortOrder = 2 });
        exercise.Datasets.Add(new() { Id = Guid.NewGuid(), Name = "First", FeedsJourney = true, SortOrder = 1 });
        exercise.Datasets.Add(new() { Id = Guid.NewGuid(), Name = "Share", FeedsJourney = false });
        exercise.Datasets.Add(new() { Id = Guid.NewGuid(), Name = "Old", FeedsJourney = true, Retired = true });

        var view = Assert.IsType<ViewResult>(await _controller.New(_window.Id, exercise.Id, default));

        Assert.Equal(["First", "Second"], Assert.IsType<AddExerciseDataItem>(view.Model).ExistingJourneyFiles);
    }

    private CheckingExerciseDto AddExercise(CheckingExerciseType? type)
    {
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = type, DisplayOnly = type is null,
            StartDate = _window.Exercises[0].StartDate, EndDate = _window.Exercises[0].EndDate
        };
        _window.Exercises.Add(exercise);
        return exercise;
    }
}
