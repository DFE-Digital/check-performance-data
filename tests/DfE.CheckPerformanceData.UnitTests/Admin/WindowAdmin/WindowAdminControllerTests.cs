using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class WindowAdminControllerTests
{
    [Fact]
    public async Task Index_takes_each_exercise_status_from_the_checking_exercise_service()
    {
        // The rule itself is pinned in CheckingExerciseServiceTests; the controller only passes
        // each exercise, with its whole window, through to it, in SortOrder.
        var service = Substitute.For<IWindowService>();
        var enquiry = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.ResultsEnquiry, StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue
        };
        var pupilData = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData, TabOrder = -1,
            StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue
        };
        var window = new CheckingWindowDto
        {
            Title = "Test window", KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue,
            Exercises = [enquiry, pupilData]
        };
        service.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult { Windows = [window] });
        var checkingExercises = Substitute.For<ICheckingExerciseService>();
        checkingExercises.StatusOf(window.Exercises, pupilData).Returns(ExerciseSchoolStatus.Visible);
        checkingExercises.StatusOf(window.Exercises, enquiry).Returns(ExerciseSchoolStatus.VisibleClosed);
        var controller = Controller(service, checkingExercises, Substitute.For<IAdminAccessPolicy>());

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var exercises = Assert.Single(Assert.IsType<WindowViewModel>(result.Model).Windows).Exercises;

        Assert.Equal([ExerciseSchoolStatus.Visible, ExerciseSchoolStatus.VisibleClosed],
            exercises.Select(e => e.Status));
    }

    // #535: the list's statuses come from the UK clock. 16:30 UTC on a summer day is 17:30 in the
    // UK — after a 17:00 end, before an 18:00 end, and before an 18:00 start. VisibleUntil keeps
    // the window shown after the exercise ends, so a closed exercise reads VisibleClosed, not Hidden.
    [Theory]
    [InlineData(9, 17, ExerciseSchoolStatus.VisibleClosed)]
    [InlineData(9, 18, ExerciseSchoolStatus.Visible)]
    [InlineData(18, 20, ExerciseSchoolStatus.Hidden)]
    public async Task Index_reads_exercise_status_on_the_UK_clock(
        int startHour, int endHour, ExerciseSchoolStatus status)
    {
        var day = new DateTime(2026, 7, 15);
        var service = Substitute.For<IWindowService>();
        service.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult
        {
            Windows = [new CheckingWindowDto
            {
                Title = "Test window", KeyStage = KeyStages.KS4,
                CheckingWindowType = CheckingWindowType.KS4June,
                StartDate = day, EndDate = day.AddDays(1),
                Exercises = [new CheckingExerciseDto
                {
                    ExerciseType = CheckingExerciseType.PupilData, IsEnabled = true,
                    StartDate = day.AddHours(startHour), EndDate = day.AddHours(endHour),
                    VisibleUntil = day.AddDays(1)
                }]
            }]
        });
        var controller = Controller(service,
            new CheckingExerciseService(new UkClockAt("2026-07-15T16:30:00Z")), Substitute.For<IAdminAccessPolicy>());

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var model = Assert.IsType<WindowViewModel>(result.Model);
        Assert.Equal(status, Assert.Single(Assert.Single(model.Windows).Exercises).Status);
    }

    [Fact]
    public async Task Index_shows_whether_each_exercise_has_data()
    {
        // The rule is pinned in ExerciseDataStatusTests; the controller copies it per exercise.
        var service = Substitute.For<IWindowService>();
        var empty = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.PupilData, StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue
        };
        var uploaded = new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.ResultsEnquiry, TabOrder = 1,
            StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue,
            Datasets = [new CheckingWindowDatasetDto { Name = "results", IngressFile = "r.csv", SchemaFile = "r.json" }]
        };
        var window = new CheckingWindowDto
        {
            Title = "Test window", KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue,
            Exercises = [empty, uploaded]
        };
        service.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult { Windows = [window] });
        var controller = Controller(service, Substitute.For<ICheckingExerciseService>(), Substitute.For<IAdminAccessPolicy>());

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var exercises = Assert.Single(Assert.IsType<WindowViewModel>(result.Model).Windows).Exercises;

        Assert.Equal([ExerciseDataStatus.NoFiles, ExerciseDataStatus.NotValidated],
            exercises.Select(e => e.DataStatus));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Index_offers_a_new_window_button_only_to_a_user_who_may_create_one(bool allowed)
    {
        // The "Create new window" nav entry is gone; this button is the way in. The wizard's
        // create steps are gated on NewWindow, so a user without it must not be shown the door.
        var service = Substitute.For<IWindowService>();
        service.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult { Windows = [] });
        var policy = Substitute.For<IAdminAccessPolicy>();
        policy.CanAccessAsync(Arg.Any<ClaimsPrincipal>(), AdminNavKeys.NewWindow).Returns(allowed);
        var controller = Controller(service, Substitute.For<ICheckingExerciseService>(), policy);

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));

        Assert.Equal(allowed, Assert.IsType<WindowViewModel>(result.Model).CanCreateWindow);
    }

    private static WindowAdminController Controller(IWindowService service,
        ICheckingExerciseService checkingExercises, IAdminAccessPolicy policy) =>
        new(service, checkingExercises, policy)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
