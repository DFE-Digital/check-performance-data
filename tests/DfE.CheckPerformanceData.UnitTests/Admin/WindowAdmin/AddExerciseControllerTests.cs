using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #466: thin by design — read the form, call IWindowService.AddExerciseAsync, show its reason or
// redirect. The rules are pinned by WindowServiceExerciseTests.
public class AddExerciseControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly IWindowService _service = Substitute.For<IWindowService>();

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId, Title = "16 to 19", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 6, 1)
    };

    private AddExerciseController Build() => new(_service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private static ExerciseFormItem Form() => new()
    {
        WindowId = WindowId, Name = "Retention", TabName = "Retention", SortOrder = 5,
        StartDate = DateTime.UtcNow.Date.AddMonths(1), StartHour = 0, StartMinute = 0,
        EndDate = DateTime.UtcNow.Date.AddMonths(2), EndHour = 17, EndMinute = 0
    };

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_window()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        Assert.IsType<NotFoundResult>(await Build().Index(WindowId, CancellationToken.None));
    }

    [Fact]
    public async Task Get_shows_an_empty_form_with_the_default_times()
    {
        _service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var view = Assert.IsType<ViewResult>(await Build().Index(WindowId, CancellationToken.None));
        var model = Assert.IsType<ExerciseFormItem>(view.Model);

        Assert.Equal("Add exercise", model.Heading);
        Assert.Equal(string.Empty, model.Name);
        Assert.Equal(ExerciseDatesItem.DefaultStartHour, model.StartHour);
        Assert.Equal(ExerciseDatesItem.DefaultEndHour, model.EndHour);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/add", model.PostUrl);
        Assert.Equal($"/admin/windows/summary/{WindowId}", model.CancelUrl);
    }

    [Fact]
    public async Task Post_calls_the_service_with_the_typed_definition_and_redirects_to_summary()
    {
        _service.AddExerciseAsync(WindowId, Arg.Any<ExerciseDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Ok());
        var form = Form();

        var result = await Build().Submit(WindowId, form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["id"]);
        await _service.Received().AddExerciseAsync(WindowId,
            Arg.Is<ExerciseDefinition>(d => d.Name == "Retention" && d.TabName == "Retention" && d.SortOrder == 5
                && d.StartDate == form.StartDate && d.EndDate == form.EndDate!.Value.AddHours(17)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_redisplays_with_the_services_reason_when_refused()
    {
        _service.AddExerciseAsync(WindowId, Arg.Any<ExerciseDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("An exercise with that name already exists in this window"));
        var controller = Build();

        var view = Assert.IsType<ViewResult>(await controller.Submit(WindowId, Form(), CancellationToken.None));

        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState[nameof(ExerciseFormItem.Name)]!.Errors,
            e => e.ErrorMessage == "An exercise with that name already exists in this window");
        Assert.Equal("Add exercise", Assert.IsType<ExerciseFormItem>(view.Model).Heading);
    }

    [Fact]
    public async Task Post_does_not_call_the_service_when_a_required_field_is_missing()
    {
        var controller = Build();
        controller.ModelState.AddModelError(nameof(ExerciseFormItem.Name), "Enter a name");

        Assert.IsType<ViewResult>(await controller.Submit(WindowId, Form(), CancellationToken.None));

        await _service.DidNotReceiveWithAnyArgs().AddExerciseAsync(default, default!, default);
    }

    [Fact]
    public async Task Post_redirects_to_summary_rather_than_redisplaying_when_the_window_is_gone()
    {
        // #466 review fix: "Window not found" has no field to blame it on, and rendering it would
        // put "Window not found" under the Name label.
        _service.AddExerciseAsync(WindowId, Arg.Any<ExerciseDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("Window not found"));

        var result = await Build().Submit(WindowId, Form(), CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Post_rejects_a_window_id_that_does_not_match_the_route()
    {
        var form = Form();
        form.WindowId = Guid.NewGuid();

        Assert.IsType<BadRequestResult>(await Build().Submit(WindowId, form, CancellationToken.None));
    }
}
