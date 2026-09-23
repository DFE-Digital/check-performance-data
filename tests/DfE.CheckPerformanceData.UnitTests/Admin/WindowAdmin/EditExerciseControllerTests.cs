using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

public class EditExerciseControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PupilDataId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly IWindowService _service = Substitute.For<IWindowService>();

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId, Title = "16 to 19", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 6, 1),
        Exercises =
        [
            new CheckingExerciseDto { Id = PupilDataId, ExerciseType = CheckingExerciseType.PupilData,
                Name = "Pupil data checking", TabName = "Pupils", SortOrder = 0,
                StartDate = new DateTime(2027, 1, 1, 9, 30, 0), EndDate = new DateTime(2027, 1, 14, 17, 0, 0) }
        ]
    };

    private EditExerciseController Build() => new(_service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public async Task Get_returns_not_found_for_an_exercise_the_window_does_not_have()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        Assert.IsType<NotFoundResult>(await Build().Index(WindowId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Get_fills_the_form_from_the_exercise_and_shows_its_kind()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var view = Assert.IsType<ViewResult>(await Build().Index(WindowId, PupilDataId, CancellationToken.None));
        var model = Assert.IsType<ExerciseFormItem>(view.Model);

        Assert.Equal("Edit Pupil data checking", model.Heading);
        Assert.Equal("Pupil data checking", model.Name);
        Assert.Equal("Pupils", model.TabName);
        Assert.Equal(0, model.SortOrder);
        Assert.Equal(new DateTime(2027, 1, 1), model.StartDate);
        Assert.Equal(9, model.StartHour);
        Assert.Equal(30, model.StartMinute);
        Assert.Equal(17, model.EndHour);
        Assert.Equal("Pupil data checking", model.KindLabel);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{PupilDataId}/edit", model.PostUrl);
    }

    [Fact]
    public async Task Get_labels_a_display_only_exercise_as_a_data_share()
    {
        var window = Window();
        var summaryId = Guid.NewGuid();
        window.Exercises.Add(new CheckingExerciseDto { Id = summaryId, ExerciseType = null, Name = "Summary data (Autumn)",
            TabName = "Summary", StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 2, 1), SortOrder = 2 });
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);

        var view = Assert.IsType<ViewResult>(await Build().Index(WindowId, summaryId, CancellationToken.None));

        Assert.Equal(CheckingExerciseNames.DisplayOnlyLabel, Assert.IsType<ExerciseFormItem>(view.Model).KindLabel);
    }

    [Fact]
    public async Task Post_calls_the_service_and_redirects_to_summary()
    {
        _service.UpdateExerciseAsync(WindowId, PupilDataId, Arg.Any<ExerciseDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Ok());
        var form = new ExerciseFormItem
        {
            WindowId = WindowId, Name = "Student data checking", TabName = "Students", SortOrder = 1,
            StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(15), EndHour = 17
        };

        var result = await Build().Submit(WindowId, PupilDataId, form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["id"]);
        await _service.Received().UpdateExerciseAsync(WindowId, PupilDataId,
            Arg.Is<ExerciseDefinition>(d => d.Name == "Student data checking" && d.TabName == "Students"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_redisplays_with_the_services_reason_when_refused()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        _service.UpdateExerciseAsync(WindowId, PupilDataId, Arg.Any<ExerciseDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("An exercise with that name already exists in this window"));
        var controller = Build();
        var form = new ExerciseFormItem
        {
            WindowId = WindowId, Name = "x", TabName = "x", SortOrder = 1,
            StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(15)
        };

        Assert.IsType<ViewResult>(await controller.Submit(WindowId, PupilDataId, form, CancellationToken.None));

        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Post_redirects_to_summary_rather_than_redisplaying_when_the_exercise_is_gone()
    {
        // #466 review fix: Redisplay's own lookup would 404 on a missing exercise anyway, discarding
        // whatever ModelState error was set, so go straight to Summary instead of that dead code path.
        _service.UpdateExerciseAsync(WindowId, PupilDataId, Arg.Any<ExerciseDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("Exercise not found"));
        var form = new ExerciseFormItem
        {
            WindowId = WindowId, Name = "x", TabName = "x", SortOrder = 1,
            StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(15)
        };

        var result = await Build().Submit(WindowId, PupilDataId, form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Post_rejects_a_window_id_that_does_not_match_the_route()
    {
        var form = new ExerciseFormItem
        {
            WindowId = Guid.NewGuid(), Name = "x", TabName = "x", SortOrder = 1,
            StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(15)
        };

        Assert.IsType<BadRequestResult>(await Build().Submit(WindowId, PupilDataId, form, CancellationToken.None));
    }
}
