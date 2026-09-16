using System.Text;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

public sealed class CheckingDataPocTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static CheckingDataExercise Exercise(bool open = true) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Provisional Students", "Students", 0,
        CheckingExerciseType.PupilData, KeyStages.Post16, true, null, null,
        Now.AddDays(-2), open ? Now.AddDays(1) : Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(1), null);

    [Fact]
    public void Visibility_has_inclusive_start_and_exclusive_end_independent_of_window()
    {
        var exercise = Exercise(false);
        Assert.True(exercise.IsVisible(Now));
        Assert.False(exercise.CanAct(Now));
        Assert.True((exercise with { VisibleFrom = Now }).IsVisible(Now));
        Assert.False((exercise with { VisibleFrom = Now.AddTicks(1) }).IsVisible(Now));
        Assert.False((exercise with { VisibleUntil = Now }).IsVisible(Now));
        Assert.True((exercise with { VisibleUntil = Now.AddTicks(1) }).IsVisible(Now));
        Assert.False((exercise with { IsEnabled = false }).IsVisible(Now));
    }

    [Theory]
    [InlineData(true, CheckingExerciseType.PupilData)]
    [InlineData(false, CheckingExerciseType.PupilData)]
    [InlineData(true, CheckingExerciseType.ResultsEnquiry)]
    [InlineData(false, CheckingExerciseType.ResultsEnquiry)]
    public async Task Visible_data_can_be_viewed_and_downloaded_but_only_open_windows_allow_actions(bool open, CheckingExerciseType type)
    {
        var exercise = Exercise(open) with { ExerciseType = type };
        var catalogue = Substitute.For<ICheckingDataCatalogue>();
        catalogue.GetVisibleAsync(default).Returns(Task.FromResult<IReadOnlyList<CheckingDataExercise>>([exercise]));
        var reader = Substitute.For<ICheckingDataReader>();
        var bytes = Encoding.UTF8.GetBytes("[{\"SURNAME\":\"A\"}]");
        reader.ReadAsync(exercise, "860/4070", default).Returns(Task.FromResult<byte[]?>(bytes));
        var user = Substitute.For<ICurrentUserService>();
        user.OrganisationLaestab.Returns("860/4070");
        var session = Substitute.For<ISession>();
        var controller = new CheckingDataController(catalogue, reader, user, new Clock())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = session } }
        };
        var model = Assert.IsType<List<CheckingDataTab>>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);
        Assert.Equal(open, Assert.Single(model).CanAct);
        Assert.Equal("A", model[0].Rows[0]["SURNAME"]);
        Assert.Equal(bytes, Assert.IsType<FileContentResult>(await controller.Download(exercise.Id, default)).FileContents);
        var action = await controller.Start(exercise.Id, default);
        if (open)
        {
            var redirect = Assert.IsType<RedirectToActionResult>(action);
            Assert.Equal(type == CheckingExerciseType.PupilData ? "WhatToChange" : "ResultIssue", redirect.ControllerName);
            session.Received().Set("SelectedWindowId", Arg.Is<byte[]>(value => Encoding.UTF8.GetString(value) == exercise.WindowId.ToString()));
            Assert.Equal(exercise.WindowId, redirect.RouteValues!["windowId"]);
        }
        else Assert.Equal(403, Assert.IsType<StatusCodeResult>(action).StatusCode);
        Assert.IsType<NotFoundResult>(await controller.Download(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Ambiguous_visible_releases_remain_readable_but_cannot_start_a_window_scoped_journey()
    {
        var first = Exercise();
        var second = first with { Id = Guid.NewGuid() };
        var catalogue = Substitute.For<ICheckingDataCatalogue>();
        catalogue.GetVisibleAsync(default).Returns(Task.FromResult<IReadOnlyList<CheckingDataExercise>>([first, second]));
        var reader = Substitute.For<ICheckingDataReader>();
        reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "860/4070", default)
            .Returns(Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("[]")));
        var user = Substitute.For<ICurrentUserService>();
        user.OrganisationLaestab.Returns("860/4070");
        var controller = new CheckingDataController(catalogue, reader, user, new Clock());
        var tabs = Assert.IsType<List<CheckingDataTab>>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);
        Assert.Equal(2, tabs.Count);
        Assert.All(tabs, tab => Assert.False(tab.CanAct));
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.Start(first.Id, default)).StatusCode);
        Assert.IsType<FileContentResult>(await controller.Download(second.Id, default));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Legacy_journey_guard_cannot_bypass_hidden_exercise_or_closed_outer_window(bool enabled, bool windowOpen)
    {
        var exercise = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Now.AddDays(-1),
            EndDate = Now.AddDays(1),
            TabName = "Students",
            IsEnabled = enabled,
            WindowStart = Now.AddDays(-1),
            WindowEnd = windowOpen ? Now.AddDays(1) : Now.AddHours(-1)
        };
        Assert.False(new CheckingExerciseService(new Clock()).IsOpen([exercise], CheckingExerciseType.PupilData));
    }
    [Theory]
    [InlineData(CheckingExerciseType.PupilData)]
    [InlineData(CheckingExerciseType.ResultsEnquiry)]
    [InlineData(null)]
    public async Task Display_only_data_remains_readable_but_cannot_start_a_journey(CheckingExerciseType? type)
    {
        var exercise = Exercise() with { ExerciseType = type, DisplayOnly = true, TabName = "Summary" };
        var catalogue = Substitute.For<ICheckingDataCatalogue>();
        catalogue.GetVisibleAsync(default).Returns(Task.FromResult<IReadOnlyList<CheckingDataExercise>>([exercise]));
        var reader = Substitute.For<ICheckingDataReader>();
        var bytes = Encoding.UTF8.GetBytes("[{\"Total\":4}]");
        reader.ReadAsync(exercise, "860/4070", default).Returns(Task.FromResult<byte[]?>(bytes));
        var user = Substitute.For<ICurrentUserService>();
        user.OrganisationLaestab.Returns("860/4070");
        var controller = new CheckingDataController(catalogue, reader, user, new Clock());

        var tabs = Assert.IsType<List<CheckingDataTab>>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);
        Assert.False(Assert.Single(tabs).CanAct);
        Assert.Equal("4", tabs[0].Rows[0]["Total"]);
        Assert.Equal(bytes, Assert.IsType<FileContentResult>(await controller.Download(exercise.Id, default)).FileContents);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.Start(exercise.Id, default)).StatusCode);
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, false)]
    [InlineData(CheckingExerciseType.PupilData, true)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, false)]
    [InlineData(CheckingExerciseType.ResultsEnquiry, true)]
    public void Display_only_exercises_offer_no_actions_deadlines_or_closed_notices(CheckingExerciseType type, bool past)
    {
        var exercise = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = type, DisplayOnly = true,
            StartDate = Now.AddDays(-2), EndDate = past ? Now.AddDays(-1) : Now.AddDays(1)
        };
        var service = new CheckingExerciseService(new Clock());
        Assert.False(service.IsOpen([exercise], type));
        Assert.False(service.HasClosed([exercise], type));
        Assert.Empty(service.OpenCheckingExercises([exercise]));
        Assert.Null(service.StartDateFor([exercise], type));
        Assert.Null(service.EndDateFor([exercise], type));
        Assert.Equal(exercise.Id, service.IdFor([exercise], type));
    }

    [Fact]
    public async Task Display_only_summary_does_not_block_an_interactive_tab_of_the_same_type()
    {
        var interactive = Exercise();
        var summary = interactive with { Id = Guid.NewGuid(), TabName = "Summary", DisplayOnly = true };
        var catalogue = Substitute.For<ICheckingDataCatalogue>();
        catalogue.GetVisibleAsync(default).Returns(Task.FromResult<IReadOnlyList<CheckingDataExercise>>([summary, interactive]));
        var reader = Substitute.For<ICheckingDataReader>();
        reader.ReadAsync(Arg.Any<CheckingDataExercise>(), "860/4070", default)
            .Returns(Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("[]")));
        var user = Substitute.For<ICurrentUserService>();
        user.OrganisationLaestab.Returns("860/4070");
        var controller = new CheckingDataController(catalogue, reader, user, new Clock())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Session = Substitute.For<ISession>() } }
        };
        var tabs = Assert.IsType<List<CheckingDataTab>>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);
        Assert.False(tabs[0].CanAct);
        Assert.True(tabs[1].CanAct);
        Assert.IsType<RedirectToActionResult>(await controller.Start(interactive.Id, default));
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.Start(summary.Id, default)).StatusCode);

        var exercises = new List<CheckingExerciseDto>
        {
            new() { Id = summary.Id, ExerciseType = summary.ExerciseType, DisplayOnly = true, StartDate = summary.ActionStart, EndDate = summary.ActionEnd },
            new() { Id = interactive.Id, ExerciseType = interactive.ExerciseType, StartDate = interactive.ActionStart, EndDate = interactive.ActionEnd }
        };
        var service = new CheckingExerciseService(new Clock());
        Assert.True(service.IsOpen(exercises, interactive.ExerciseType!.Value));
        Assert.Equal(interactive.Id, service.IdFor(exercises, interactive.ExerciseType!.Value));
        var windows = Substitute.For<IWindowRepository>();
        windows.GetByIdAsync(interactive.WindowId, default).Returns(new CheckingWindowDto
        {
            Id = interactive.WindowId, Title = "Checking", KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16, StartDate = interactive.WindowStart,
            EndDate = interactive.WindowEnd, Exercises = exercises
        });
        var resolved = await new CheckingExerciseStorageResolver(windows, new Clock()).ResolveAsync(interactive.WindowId, interactive.ExerciseType!.Value);
        Assert.Equal(interactive.Id, resolved!.Id);
    }

    [Fact]
    public void Untyped_summary_has_no_open_journey_types()
    {
        var summary = new CheckingExerciseDto
        {
            ExerciseType = null, DisplayOnly = true,
            StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1)
        };
        var service = new CheckingExerciseService(new Clock());
        Assert.Empty(service.OpenCheckingExercises([summary]));
        foreach (var type in Enum.GetValues<CheckingExerciseType>())
            Assert.False(service.IsOpen([summary], type));
    }

}
