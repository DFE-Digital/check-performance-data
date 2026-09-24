using System.ComponentModel.DataAnnotations;
using System.Reflection;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public sealed class EditCheckingExerciseControllerTests
{
    private readonly CheckingWindowDto _window = CreateCheckingExerciseControllerTests.Window();
    private readonly IWindowService _service = Substitute.For<IWindowService>();
    private readonly EditCheckingExerciseController _controller;

    public EditCheckingExerciseControllerTests()
    {
        _service.GetByIdAsync(_window.Id, Arg.Any<CancellationToken>()).Returns(_window);
        var urls = Substitute.For<IUrlHelper>();
        urls.Action(Arg.Any<UrlActionContext>()).Returns(call =>
        {
            var action = call.Arg<UrlActionContext>();
            return $"/{action.Controller}/{action.Action}";
        });
        _controller = new(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = urls
        };
    }

    private async Task<CreateCheckingExerciseItem> Model(Guid? exerciseId = null) =>
        Assert.IsType<CreateCheckingExerciseItem>(Assert.IsType<ViewResult>(
            await _controller.Edit(_window.Id, exerciseId ?? _window.Exercises[0].Id, default)).Model);

    [Fact]
    public async Task Edit_prefills_the_selected_exercise_and_excludes_it_from_replacements()
    {
        var exercise = _window.Exercises[0];
        var model = await Model();
        Assert.True(model.IsEditing);
        Assert.Equal("Edit checking exercise", model.PageTitle);
        Assert.Equal("Save changes", model.SubmitLabel);
        Assert.Equal(exercise.Name, model.Name);
        Assert.Equal(exercise.ExerciseType, model.ExerciseType);
        Assert.Equal(exercise.StartDate, model.Dates.StartDate);
        Assert.Equal(exercise.StartDate.Hour, model.Dates.StartHour);
        Assert.Equal(exercise.EndDate, model.Dates.EndDate);
        Assert.Equal(_window.Id, model.WindowId);
        Assert.Equal("/EditCheckingExercise/Update", model.PostUrl);
        Assert.Equal("/Summary/Index", model.CancelUrl);
        Assert.Empty(model.ReplacementOptions);
    }

    [Fact]
    public async Task Update_changes_only_the_selected_exercise_and_preserves_files_and_validation()
    {
        var original = _window.Exercises[0];
        original.ValidatedAt = DateTime.Now;
        original.Datasets = [new() { Id = Guid.NewGuid(), Name = "custom", IngressFile = "file.csv", SchemaFile = "schema.json" }];
        var other = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = original.ExerciseType,
            Name = "Other release", StartDate = original.StartDate, EndDate = original.EndDate
        };
        _window.Exercises.Add(other);
        var model = await Model();
        model.Name = " Updated name ";
        model.TabName = " Students ";
        model.TabOrder = 4;
        model.SortOrder = 5;
        model.IsEnabled = true;
        model.VisibleFrom = original.StartDate.AddHours(9);
        model.VisibleUntil = original.EndDate.AddHours(17);
        model.ReplacesCheckingExerciseId = other.Id;
        model.Layout = DfE.CheckPerformanceData.Application.CheckYourPupilData.ExerciseLayout.Vertical;
        Validate(model);

        var redirect = Assert.IsType<RedirectToActionResult>(
            await _controller.Update(_window.Id, original.Id, model, default));
        var updated = _window.Exercises[0];
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("Updated name", updated.Name);
        Assert.Equal("Students", updated.TabName);
        Assert.Equal(4, updated.TabOrder);
        Assert.Equal(5, updated.SortOrder);
        Assert.True(updated.IsEnabled);
        Assert.Equal(model.VisibleFrom, updated.VisibleFrom);
        Assert.Equal(model.VisibleUntil, updated.VisibleUntil);
        Assert.Equal(other.Id, updated.ReplacesCheckingExerciseId);
        Assert.Equal(DfE.CheckPerformanceData.Application.CheckYourPupilData.ExerciseLayout.Vertical, updated.Layout);
        Assert.Same(original.Datasets, updated.Datasets);
        Assert.Equal(original.ValidatedAt, updated.ValidatedAt);
        Assert.Same(other, _window.Exercises[1]);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(_window.Id, redirect.RouteValues!["id"]);
        await _service.Received(1).UpdateAsync(_window, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Existing_past_dates_can_be_kept_when_editing_details()
    {
        var original = _window.Exercises[0];
        original.StartDate = new DateTime(2020, 1, 1, 9, 30, 15);
        original.EndDate = new DateTime(2020, 2, 1, 17, 0, 30);
        var model = await Model();
        model.Name = "Renamed";
        Validate(model);
        Assert.IsType<RedirectToActionResult>(await _controller.Update(_window.Id, original.Id, model, default));
        Assert.Equal(original.StartDate, _window.Exercises[0].StartDate);
        Assert.Equal(original.EndDate, _window.Exercises[0].EndDate);
    }

    [Fact]
    public async Task Changing_exercise_type_clears_validation_but_preserves_datasets()
    {
        var original = _window.Exercises[0];
        original.ValidatedAt = DateTime.Now;
        var model = await Model();
        model.ExerciseType = CheckingExerciseType.ResultsEnquiry;
        Validate(model);
        await _controller.Update(_window.Id, original.Id, model, default);
        Assert.Equal(CheckingExerciseType.ResultsEnquiry, _window.Exercises[0].ExerciseType);
        Assert.Null(_window.Exercises[0].ValidatedAt);
        Assert.Same(original.Datasets, _window.Exercises[0].Datasets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Self_or_cyclic_replacement_is_rejected(bool indirect)
    {
        var original = _window.Exercises[0];
        var other = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = original.ExerciseType,
            StartDate = original.StartDate, EndDate = original.EndDate,
            ReplacesCheckingExerciseId = original.Id
        };
        _window.Exercises.Add(other);
        var model = await Model();
        Assert.Empty(model.ReplacementOptions);
        model.ReplacesCheckingExerciseId = indirect ? other.Id : original.Id;
        Assert.IsType<ViewResult>(await _controller.Update(_window.Id, original.Id, model, default));
        Assert.Contains(nameof(model.ReplacesCheckingExerciseId), _controller.ModelState.Keys);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Missing_or_foreign_exercise_and_mismatched_window_do_not_save()
    {
        var foreignId = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await _controller.Edit(_window.Id, foreignId, default));
        Assert.IsType<NotFoundResult>(await _controller.Edit(Guid.NewGuid(), _window.Exercises[0].Id, default));
        var model = await Model();
        Assert.IsType<NotFoundResult>(await _controller.Update(_window.Id, foreignId, model, default));
        Assert.IsType<BadRequestResult>(await _controller.Update(Guid.NewGuid(), _window.Exercises[0].Id, model, default));
        model.ReplacesCheckingExerciseId = foreignId;
        Assert.IsType<ViewResult>(await _controller.Update(_window.Id, _window.Exercises[0].Id, model, default));
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("ExerciseType")]
    [InlineData("Dates.EndDate")]
    [InlineData("VisibleUntil")]
    public async Task Invalid_edit_redisplays_entered_values_and_edit_urls(string field)
    {
        var model = await Model();
        switch (field)
        {
            case "Name": model.Name = ""; break;
            case "ExerciseType": model.ExerciseType = (CheckingExerciseType)999; break;
            case "Dates.EndDate": model.Dates.EndDate = model.Dates.StartDate!.Value.AddDays(-1); break;
            case "VisibleUntil": model.VisibleFrom = DateTime.Today; model.VisibleUntil = DateTime.Today; break;
        }
        Validate(model);
        var view = Assert.IsType<ViewResult>(await _controller.Update(_window.Id, _window.Exercises[0].Id, model, default));
        Assert.Same(model, view.Model);
        Assert.Contains(field, _controller.ModelState.Keys);
        Assert.True(model.IsEditing);
        Assert.Equal("/EditCheckingExercise/Update", model.PostUrl);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_edit_preserves_storage_and_rejects_type_changes(bool changeType)
    {
        var original = _window.Exercises[0];
        _window.Exercises[0] = new CheckingExerciseDto
        {
            Id = original.Id, UsesExerciseStorage = false, ExerciseType = original.ExerciseType,
            StartDate = original.StartDate, EndDate = original.EndDate
        };
        var model = await Model();
        Assert.True(model.TabNameOptional);
        if (changeType) model.ExerciseType = CheckingExerciseType.ResultsEnquiry;
        Validate(model);
        var result = await _controller.Update(_window.Id, original.Id, model, default);
        if (changeType)
        {
            Assert.IsType<ViewResult>(result);
            await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        }
        else
        {
            Assert.IsType<RedirectToActionResult>(result);
            Assert.False(_window.Exercises[0].UsesExerciseStorage);
            Assert.Null(_window.Exercises[0].TabName);
        }
    }

    [Fact]
    public void Edit_requires_admin_access_and_antiforgery()
    {
        Assert.Equal(AdminNavKeys.ManageWindow,
            typeof(EditCheckingExerciseController).GetCustomAttribute<RequireAdminSectionAttribute>()!.SectionKey);
        Assert.NotNull(typeof(EditCheckingExerciseController).GetMethod("Update")!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private void Validate(CreateCheckingExerciseItem model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        foreach (var result in results)
            foreach (var member in result.MemberNames)
                _controller.ModelState.AddModelError(member, result.ErrorMessage!);
    }
    [Fact]
    public async Task Display_only_can_be_saved_reloaded_and_turned_off()
    {
        var model = await Model();
        model.DisplayOnly = true;
        Assert.IsType<RedirectToActionResult>(await _controller.Update(_window.Id, _window.Exercises[0].Id, model, default));
        Assert.True((await Model()).DisplayOnly);
        model = await Model();
        model.DisplayOnly = false;
        Assert.IsType<RedirectToActionResult>(await _controller.Update(_window.Id, _window.Exercises[0].Id, model, default));
        Assert.False((await Model()).DisplayOnly);
    }

    [Fact]
    public async Task Display_only_type_can_be_cleared_but_journeys_cannot_be_enabled_without_a_type()
    {
        var model = await Model();
        model.DisplayOnly = true;
        model.ExerciseType = null;
        Validate(model);
        Assert.True(_controller.ModelState.IsValid);
        Assert.IsType<RedirectToActionResult>(await _controller.Update(_window.Id, _window.Exercises[0].Id, model, default));
        model = await Model();
        Assert.Null(model.ExerciseType);
        Assert.True(model.DisplayOnly);
        model.DisplayOnly = false;
        Validate(model);
        Assert.False(_controller.ModelState.IsValid);
        Assert.Contains("ExerciseType", _controller.ModelState.Keys);
        Assert.IsType<ViewResult>(await _controller.Update(_window.Id, _window.Exercises[0].Id, model, default));
        Assert.True(_window.Exercises[0].DisplayOnly);
    }

}
