using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #319: "Which checking exercises does this window run?". The page lists every CheckingExerciseType
// and pre-ticks the window type's defaults, which is what makes both acceptance criteria hold at
// once — a new enum member surfaces without a rewrite, and a single-exercise window is one Continue.
public class ExercisesControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public ExercisesControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }

    [Fact]
    public void New_get_lists_every_exercise_type_that_exists()
    {
        // The list comes from the enum, so a type added later appears here without this page or
        // this controller being touched.
        ExercisesItem model = NewModel(Draft(CheckingWindowType.KS4June));

        Assert.Equal(Enum.GetValues<CheckingExerciseType>().Length, model.All.Count);
    }

    [Fact]
    public void New_get_pre_ticks_the_window_types_defaults()
    {
        ExercisesItem model = NewModel(Draft(CheckingWindowType.Post16));

        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            model.Selected);
    }

    [Fact]
    public void New_get_pre_ticks_only_pupil_data_for_a_single_exercise_window_type()
    {
        ExercisesItem model = NewModel(Draft(CheckingWindowType.KS4June));

        Assert.Equal(CheckingExerciseType.PupilData, Assert.Single(model.Selected));
    }

    [Fact]
    public void New_get_keeps_the_admins_own_choice_on_a_revisit()
    {
        // Coming back to change one box must not silently reset the others to the type's defaults.
        CheckingWindowDraft draft = Draft(CheckingWindowType.Post16);
        draft.Exercises = [new ExerciseDraft { ExerciseType = CheckingExerciseType.ResultsEnquiry, SortOrder = 1 }];

        ExercisesItem model = NewModel(draft);

        Assert.Equal(CheckingExerciseType.ResultsEnquiry, Assert.Single(model.Selected));
    }

    [Fact]
    public void New_post_rejects_an_empty_selection()
    {
        CheckingWindowDraft draft = Draft(CheckingWindowType.Post16);
        ExercisesController controller = Build(Substitute.For<IWindowService>(), SessionWithDraft(draft));

        IActionResult result = controller.Submit(new ExercisesItem { Selected = [] });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ErrorCount > 0);
    }

    [Fact]
    public void New_post_stores_the_selection_in_sort_order()
    {
        ISession session = SessionWithDraft(Draft(CheckingWindowType.Post16));
        ExercisesController controller = Build(Substitute.For<IWindowService>(), session);

        controller.Submit(new ExercisesItem
        {
            Selected = [CheckingExerciseType.ResultsEnquiry, CheckingExerciseType.PupilData]
        });

        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            SavedDraft(session).Exercises.Select(e => e.ExerciseType));
    }

    [Fact]
    public void New_post_keeps_dates_already_given_for_an_exercise_that_stays_ticked()
    {
        CheckingWindowDraft draft = Draft(CheckingWindowType.Post16);
        draft.Exercises =
        [
            new ExerciseDraft
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 14)
            }
        ];
        ISession session = SessionWithDraft(draft);
        ExercisesController controller = Build(Substitute.For<IWindowService>(), session);

        controller.Submit(new ExercisesItem
        {
            Selected = [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry]
        });

        CheckingWindowDraft saved = SavedDraft(session);
        Assert.Equal(new DateTime(2027, 1, 1),
            saved.Exercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData).StartDate);
        Assert.Null(
            saved.Exercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry).StartDate);
    }

    // ── Edit (existing window) ───────────────────────────────────────────────

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
            SortOrder = 1
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

    private ExercisesItem NewModel(CheckingWindowDraft draft)
    {
        ExercisesController controller = Build(Substitute.For<IWindowService>(), SessionWithDraft(draft));
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(controller.New());
        return Assert.IsType<ExercisesItem>(view.Model);
    }

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

    private static CheckingWindowDraft Draft(CheckingWindowType type) => new()
    {
        Title = "A window",
        CheckingWindowType = type,
        KeyStage = KeyStages.KS4
    };

    private static ISession SessionWithDraft(CheckingWindowDraft draft)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft));
        ISession session = Substitute.For<ISession>();
        session.TryGetValue("CheckingWindowDraft", out Arg.Any<byte[]>())
            .Returns(call =>
            {
                call[1] = bytes;
                return true;
            });
        return session;
    }

    private static CheckingWindowDraft SavedDraft(ISession session)
    {
        byte[] written = (byte[])session.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(ISession.Set))
            .GetArguments()[1]!;
        return JsonSerializer.Deserialize<CheckingWindowDraft>(Encoding.UTF8.GetString(written))!;
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
                SortOrder = 0,
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
