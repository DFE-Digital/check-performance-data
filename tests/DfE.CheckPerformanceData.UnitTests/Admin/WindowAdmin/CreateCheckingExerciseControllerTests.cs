using System.ComponentModel.DataAnnotations;
using System.Reflection;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public sealed class CreateCheckingExerciseControllerTests
{
    private static readonly DateTime Today = new(2026, 9, 15);
    private readonly IWindowService _service = Substitute.For<IWindowService>();
    private readonly CheckingWindowDto _window = Window();
    private readonly CreateCheckingExerciseController _controller;

    public CreateCheckingExerciseControllerTests()
    {
        _service.GetByIdAsync(_window.Id, Arg.Any<CancellationToken>()).Returns(_window);
        var urls = Substitute.For<IUrlHelper>();
        urls.Action(Arg.Any<UrlActionContext>()).Returns(call =>
        {
            var action = call.Arg<UrlActionContext>();
            return $"/{action.Controller}/{action.Action}";
        });
        _controller = new(_service, new Clock())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = urls
        };
    }

    [Fact]
    public async Task New_loads_parent_and_its_replacement_options()
    {
        var model = Assert.IsType<CreateCheckingExerciseItem>(
            Assert.IsType<ViewResult>(await _controller.New(_window.Id, default)).Model);
        Assert.Equal(_window.Id, model.WindowId);
        Assert.Equal(_window.Title, model.WindowTitle);
        Assert.Equal(_window.Exercises, model.ReplacementOptions);
        Assert.Equal("/Summary/Index", model.CancelUrl);
        Assert.False(model.IsEnabled);
    }

    [Fact]
    public async Task Missing_parent_returns_not_found_on_get_and_post()
    {
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await _controller.New(id, default));
        Assert.IsType<NotFoundResult>(await _controller.Submit(id, new() { WindowId = id }, default));
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Different_posted_parent_is_rejected()
    {
        Assert.IsType<BadRequestResult>(await _controller.Submit(Guid.NewGuid(), Valid(), default));
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Creates_distinct_release_with_metadata_and_defaults_and_returns_to_parent()
    {
        var previous = Assert.Single(_window.Exercises);
        var model = Valid();
        model.ReplacesCheckingExerciseId = previous.Id;
        Validate(model);
        var redirect = Assert.IsType<RedirectToActionResult>(
            await _controller.Submit(_window.Id, model, default));
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(_window.Id, redirect.RouteValues!["id"]);
        var created = Assert.Single(_window.Exercises, e => e.Id != previous.Id);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Same(previous, _window.Exercises[0]);
        Assert.Equal("Revised students", created.Name);
        Assert.Equal("Students", created.TabName);
        Assert.Equal(previous.Id, created.ReplacesCheckingExerciseId);
        Assert.Equal(2, created.TabOrder);
        Assert.Equal(3, created.SortOrder);
        Assert.True(created.IsEnabled);
        Assert.True(created.UsesExerciseStorage);
        Assert.Equal(Today.AddDays(1).AddHours(9).AddMinutes(30), created.StartDate);
        Assert.Equal(model.VisibleFrom, created.VisibleFrom);
        Assert.Equal(model.VisibleUntil, created.VisibleUntil);
        Assert.Equal(new[] { WindowDatasets.Included, WindowDatasets.NonIncluded }, created.Datasets.Select(d => d.Name));
        Assert.Null(created.ValidatedAt);
        Assert.Equal(Today, _window.StartDate);
        Assert.Equal(Today.AddMonths(1), _window.EndDate);
        await _service.Received(1).UpdateAsync(_window, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("ExerciseType")]
    [InlineData("TabName")]
    [InlineData("TabOrder")]
    [InlineData("SortOrder")]
    [InlineData("Dates.StartDate")]
    [InlineData("Dates.EndDate")]
    public async Task Missing_required_field_redisplays_without_saving(string field)
    {
        var model = Valid();
        switch (field)
        {
            case "Name": model.Name = " "; break;
            case "ExerciseType": model.ExerciseType = null; break;
            case "TabName": model.TabName = " "; break;
            case "TabOrder": model.TabOrder = null; break;
            case "SortOrder": model.SortOrder = null; break;
            case "Dates.StartDate": model.Dates.StartDate = null; break;
            case "Dates.EndDate": model.Dates.EndDate = null; break;
        }
        Validate(model);
        Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, model, default));
        Assert.Contains(field, _controller.ModelState.Keys);
        Assert.Equal("/CreateCheckingExercise/Submit", model.PostUrl);
        Assert.Equal("/Summary/Index", model.CancelUrl);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("TabName")]
    [InlineData("ExerciseType")]
    [InlineData("TabOrder")]
    [InlineData("SortOrder")]
    [InlineData("Dates.StartHour")]
    [InlineData("Dates.EndMinute")]
    [InlineData("Dates.EndDate")]
    [InlineData("VisibleUntil")]
    public async Task Invalid_fields_are_rejected(string field)
    {
        var model = Valid();
        switch (field)
        {
            case "Name": model.Name = new string('a', 201); break;
            case "TabName": model.TabName = new string('a', 101); break;
            case "ExerciseType": model.ExerciseType = (CheckingExerciseType)999; break;
            case "TabOrder": model.TabOrder = -1; break;
            case "SortOrder": model.SortOrder = -1; break;
            case "Dates.StartHour": model.Dates.StartHour = 24; break;
            case "Dates.EndMinute": model.Dates.EndMinute = 60; break;
            case "Dates.EndDate": model.Dates.EndDate = Today; break;
            case "VisibleUntil": model.VisibleUntil = model.VisibleFrom; break;
        }
        Validate(model);
        Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, model, default));
        Assert.Contains(field, _controller.ModelState.Keys);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Past_start_is_rejected_using_local_clock()
    {
        var model = Valid();
        model.Dates.StartDate = Today.AddDays(-1);
        Validate(model);
        Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, model, default));
        Assert.Contains("Dates.StartDate", _controller.ModelState.Keys);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Replacement_from_another_window_or_missing_exercise_is_rejected()
    {
        var other = Window();
        var model = Valid();
        model.ReplacesCheckingExerciseId = other.Exercises[0].Id;
        Validate(model);
        Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, model, default));
        Assert.Contains(nameof(model.ReplacesCheckingExerciseId), _controller.ModelState.Keys);
        Assert.DoesNotContain(model.ReplacementOptions, e => e.Id == model.ReplacesCheckingExerciseId);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Existing_binding_error_preserves_values_and_does_not_save()
    {
        var model = Valid();
        _controller.ModelState.AddModelError("Dates.StartDate", "Enter a real date");
        var view = Assert.IsType<ViewResult>(await _controller.Submit(_window.Id, model, default));
        Assert.Same(model, view.Model);
        Assert.Equal("  Revised students  ", model.Name);
        Assert.Equal(_window.Exercises, model.ReplacementOptions);
        await _service.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public void Creation_requires_manage_window_access_and_antiforgery()
    {
        Assert.Equal(AdminNavKeys.ManageWindow,
            typeof(CreateCheckingExerciseController).GetCustomAttribute<RequireAdminSectionAttribute>()!.SectionKey);
        Assert.Equal(AdminNavKeys.ManageWindow,
            typeof(SummaryController).GetCustomAttribute<RequireAdminSectionAttribute>()!.SectionKey);
        Assert.NotNull(typeof(CreateCheckingExerciseController).GetMethod("Submit")!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private CreateCheckingExerciseItem Valid() => new()
    {
        WindowId = _window.Id, Name = "  Revised students  ",
        ExerciseType = CheckingExerciseType.PupilData,
        TabName = " Students ", TabOrder = 2, SortOrder = 3, IsEnabled = true,
        VisibleFrom = Today, VisibleUntil = Today.AddMonths(2),
        Dates = new ExerciseDatesItem
        {
            StartDate = Today.AddDays(1), StartHour = 9, StartMinute = 30,
            EndDate = Today.AddDays(10), EndHour = 17
        }
    };

    private void Validate(CreateCheckingExerciseItem model)
    {
        ValidateObject(model, "");
        ValidateObject(model.Dates, "Dates.");
    }

    private void ValidateObject(object model, string prefix)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        foreach (var result in results)
            foreach (var member in result.MemberNames)
                _controller.ModelState.AddModelError(prefix + member, result.ErrorMessage!);
    }

    internal static CheckingWindowDto Window() => new()
    {
        Id = Guid.NewGuid(), Title = "Pupil data checking",
        StartDate = Today, EndDate = Today.AddMonths(1),
        KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        Exercises = [new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), Name = "Provisional students", ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Today, EndDate = Today.AddDays(5), SortOrder = 0
        }]
    };

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Today, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

public sealed class CheckingExerciseSummaryControllerTests
{
    [Fact]
    public async Task Summary_lists_only_selected_windows_exercises_in_order_including_repeated_types()
    {
        var first = CreateCheckingExerciseControllerTests.Window();
        var second = CreateCheckingExerciseControllerTests.Window();
        first.Exercises.Add(new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), Name = "Revised students", TabName = "Students",
            IsEnabled = true,
            ExerciseType = CheckingExerciseType.PupilData, StartDate = first.StartDate, EndDate = first.EndDate,
            SortOrder = -1
        });
        var service = Substitute.For<IWindowService>();
        service.GetByIdAsync(first.Id, Arg.Any<CancellationToken>()).Returns(first);
        service.GetByIdAsync(second.Id, Arg.Any<CancellationToken>()).Returns(second);
        var view = Assert.IsType<ViewResult>(await new SummaryController(service).Index(first.Id, default));
        var model = Assert.IsType<WindowEditItem>(view.Model);
        Assert.Equal(first.Id, model.WindowId);
        Assert.Equal(new[] { "Revised students", "Provisional students" }, model.Exercises.Select(e => e.Label));
        Assert.All(model.Exercises, e => Assert.Equal(first.Id, e.WindowId));
        Assert.DoesNotContain(model.Exercises, e => e.ExerciseId == second.Exercises[0].Id);
        Assert.Equal("Students", model.Exercises[0].TabName);
        Assert.True(model.Exercises[0].IsEnabled);
        Assert.Contains(first.Exercises[1].Id.ToString(), model.Exercises[0].DatesLink);
        Assert.Equal($"/admin/windows/{first.Id}/exercises/new", model.AddExerciseLink);
    }

    [Fact]
    public async Task Summary_handles_empty_and_missing_windows()
    {
        var window = CreateCheckingExerciseControllerTests.Window();
        window.Exercises.Clear();
        var service = Substitute.For<IWindowService>();
        service.GetByIdAsync(window.Id, Arg.Any<CancellationToken>()).Returns(window);
        var controller = new SummaryController(service);
        var view = Assert.IsType<ViewResult>(await controller.Index(window.Id, default));
        Assert.Empty(Assert.IsType<WindowEditItem>(view.Model).Exercises);
        Assert.IsType<NotFoundResult>(await controller.Index(Guid.NewGuid(), default));
    }
}
