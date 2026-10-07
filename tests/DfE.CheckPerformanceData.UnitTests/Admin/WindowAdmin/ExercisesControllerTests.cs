using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #319: "Which checking exercises does this window run?" on an existing window. The page lists every
// CheckingExerciseType, so a new enum member surfaces without a rewrite. The create wizard has no
// exercise step: the window type gives a new window its default exercises.
public class ExercisesControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public ExercisesControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }

    [Fact]
    public async Task Edit_get_flags_the_exercises_that_already_hold_files()
    {
        // Unticking one throws its ingress and schema files away, so the page has to be able to say
        // so before the admin does it.
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(WindowWithFiles());

        ExercisesController controller = Build(windowService, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(await controller.Edit(WindowId, CancellationToken.None));
        ExercisesItem model = Assert.IsType<ExercisesItem>(view.Model);

        Assert.Equal(CheckingExerciseType.PupilData, Assert.Single(model.WithFiles));
    }

    [Fact]
    public async Task Edit_post_adds_a_newly_ticked_exercise_on_the_windows_dates_as_a_placeholder()
    {
        // A new exercise must never be left with no dates at all — the union that derives the outer
        // pair could not survive it. The admin then edits them.
        IWindowService windowService = Substitute.For<IWindowService>();
        CheckingWindowDto window = WindowWithFiles();
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);

        ExercisesController controller = Build(windowService, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        await controller.Update(WindowId, new ExercisesItem
        {
            Selected = [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry]
        }, CancellationToken.None);

        CheckingExerciseDto added = window.FindExercise(CheckingExerciseType.ResultsEnquiry)!;
        Assert.Equal(window.StartDate, added.StartDate);
        Assert.Equal(window.EndDate, added.EndDate);
        await windowService.Received(1).UpdateAsync(window, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_post_drops_an_unticked_exercise()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        CheckingWindowDto window = WindowWithFiles();
        window.Exercises.Add(new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.ResultsEnquiry,
            StartDate = window.StartDate,
            EndDate = window.EndDate,
            TabOrder = 1
        });
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);

        ExercisesController controller = Build(windowService, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        await controller.Update(WindowId,
            new ExercisesItem { Selected = [CheckingExerciseType.PupilData] }, CancellationToken.None);

        Assert.Equal(CheckingExerciseType.PupilData, Assert.Single(window.Exercises).ExerciseType);
    }

    [Fact]
    public async Task Edit_post_rejects_an_empty_selection_and_saves_nothing()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(WindowWithFiles());

        ExercisesController controller = Build(windowService, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        IActionResult result = await controller.Update(WindowId,
            new ExercisesItem { Selected = [] }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        await windowService.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private ExercisesController Build(IWindowService windowService, ISession session)
    {
        ExercisesController controller = new(windowService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            }
        };
        controller.Url = _urlHelper;
        return controller;
    }

    private static CheckingWindowDto WindowWithFiles() => new()
    {
        Id = WindowId,
        Title = "A window",
        StartDate = new DateTime(2027, 1, 1),
        EndDate = new DateTime(2027, 2, 1, 17, 0, 0),
        KeyStage = KeyStages.Post16,
        CheckingWindowType = CheckingWindowType.Post16,
        Exercises =
        [
            new CheckingExerciseDto
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 15, 17, 0, 0),
                TabOrder = 0,
                Datasets =
                [
                    new CheckingWindowDatasetDto
                    {
                        Name = "pupils", SortOrder = 0,
                        IngressFile = "pupils.csv", SchemaFile = "pupils.json"
                    }
                ]
            }
        ]
    };
}
