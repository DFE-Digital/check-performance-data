using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Admin;
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
