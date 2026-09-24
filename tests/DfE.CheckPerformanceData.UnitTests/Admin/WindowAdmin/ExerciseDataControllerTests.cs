using System.ComponentModel.DataAnnotations;
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
            (typeof(ExerciseDataController), "Submit"), (typeof(IngressFileController), "Select"),
            (typeof(SchemaController), "Submit"), (typeof(ValidateWindowController), "Validate")
        })
            Assert.NotNull(Attribute.GetCustomAttribute(controller.GetMethod(action)!, typeof(ValidateAntiForgeryTokenAttribute)));
    }

    [Fact]
    public async Task Duplicate_names_and_unknown_sources_do_not_save()
    {
        // A results source is asked only on a results enquiry.
        var owner = AddExercise(CheckingExerciseType.ResultsEnquiry);
        owner.Datasets.Add(new() { Name = "existing" });
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "existing", SourceFile = "unknown" };
        var result = Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, owner.Id, model, default));
        Assert.Same(model, result.Model);
        Assert.Contains("Name", _controller.ModelState.Keys);
        Assert.Contains("SourceFile", _controller.ModelState.Keys);
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

    [Theory]
    [InlineData(CheckingExerciseType.ResultsEnquiry)]
    [InlineData(null)]
    public async Task A_file_added_to_a_results_enquiry_or_a_data_share_is_display_only(CheckingExerciseType? type)
    {
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = type, DisplayOnly = type is null,
            StartDate = _window.Exercises[0].StartDate, EndDate = _window.Exercises[0].EndDate
        };
        _window.Exercises.Add(exercise);
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "extra-share", Inclusion = "file" };

        await _controller.Submit(_window.Id, exercise.Id, model, default);

        Assert.False(Assert.Single(exercise.Datasets).FeedsJourney);
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
    [InlineData(CheckingExerciseType.PupilData, true, false)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, false, true)]
    [InlineData(null, false, false)]
    public async Task Each_question_is_asked_only_on_the_exercise_it_applies_to(
        CheckingExerciseType? type, bool asksInclusion, bool asksSource)
    {
        var exercise = AddExercise(type);

        var page = Assert.IsType<AddExerciseDataItem>(
            Assert.IsType<ViewResult>(await _controller.New(_window.Id, exercise.Id, default)).Model);

        Assert.Equal(asksInclusion, page.AsksInclusion);
        Assert.Equal(asksSource, page.AsksSource);
        Assert.Equal(asksSource, page.SourceOptions.Count > 0);
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, true, false)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, false, true)]
    [InlineData(null, false, false)]
    public async Task A_value_for_a_question_not_asked_is_not_stored(
        CheckingExerciseType? type, bool storesInclusion, bool storesSource)
    {
        var exercise = AddExercise(type);
        var source = WindowDatasets.DefaultsFor(_window.CheckingWindowType, CheckingExerciseType.ResultsEnquiry)
            .Select(d => d.SourceFile).OfType<string>().First();
        // A hand-made post can send both.
        var model = new AddExerciseDataItem { WindowId = _window.Id, Name = "File", Inclusion = "included", SourceFile = source };

        Assert.IsType<RedirectToActionResult>(await _controller.Submit(_window.Id, exercise.Id, model, default));

        var added = Assert.Single(exercise.Datasets);
        Assert.Equal(storesInclusion ? true : null, added.Included);
        Assert.Equal(storesSource ? source : null, added.SourceFile);
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
