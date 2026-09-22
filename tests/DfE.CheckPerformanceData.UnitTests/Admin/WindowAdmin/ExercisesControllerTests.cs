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

// #319, #466: "Which checking exercises does this window run?". The page lists the window type's
// templates and pre-ticks them all, plus (on an existing window) any exercise the admin added by
// hand — which is what makes both acceptance criteria hold at once: a new template surfaces without
// a rewrite, and a single-exercise window is one Continue.
public class ExercisesControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();

    public ExercisesControllerTests()
    {
        _urlHelper.Action(Arg.Any<UrlActionContext>()).Returns("/dummy-url");
    }

    [Fact]
    public void New_get_lists_the_window_types_templates()
    {
        ExercisesItem model = NewModel(Draft(CheckingWindowType.KS4June));

        Assert.Equal(
            WindowExercises.DefaultsFor(CheckingWindowType.KS4June).Select(t => t.Name),
            model.All.Select(c => c.Name));
    }

    [Fact]
    public void New_get_pre_ticks_the_window_types_defaults()
    {
        ExercisesItem model = NewModel(Draft(CheckingWindowType.Post16));

        Assert.Equal(
            ["Pupil data checking", "Results enquiry", "Summary data (Autumn)"],
            model.Selected);
    }

    [Fact]
    public void New_get_pre_ticks_only_pupil_data_for_a_single_exercise_window_type()
    {
        ExercisesItem model = NewModel(Draft(CheckingWindowType.KS4June));

        Assert.Equal("Pupil data checking", Assert.Single(model.Selected));
    }

    [Fact]
    public void New_get_keeps_the_admins_own_choice_on_a_revisit()
    {
        // Coming back to change one box must not silently reset the others to the type's defaults.
        CheckingWindowDraft draft = Draft(CheckingWindowType.Post16);
        draft.Exercises =
        [
            new ExerciseDraft
            {
                ExerciseType = CheckingExerciseType.ResultsEnquiry, Name = "Results enquiry", SortOrder = 1
            }
        ];

        ExercisesItem model = NewModel(draft);

        Assert.Equal("Results enquiry", Assert.Single(model.Selected));
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
            Selected = ["Results enquiry", "Pupil data checking"]
        });

        Assert.Equal(
            ["Pupil data checking", "Results enquiry"],
            SavedDraft(session).Exercises.Select(e => e.Name));
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
                Name = "Pupil data checking",
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 14)
            }
        ];
        ISession session = SessionWithDraft(draft);
        ExercisesController controller = Build(Substitute.For<IWindowService>(), session);

        controller.Submit(new ExercisesItem
        {
            Selected = ["Pupil data checking", "Results enquiry"]
        });

        CheckingWindowDraft saved = SavedDraft(session);
        Assert.Equal(new DateTime(2027, 1, 1),
            saved.Exercises.Single(e => e.Name == "Pupil data checking").StartDate);
        Assert.Null(
            saved.Exercises.Single(e => e.Name == "Results enquiry").StartDate);
    }

    [Fact]
    public void New_post_keeps_dates_already_given_to_a_display_only_exercise()
    {
        CheckingWindowDraft draft = Draft(CheckingWindowType.Post16);
        draft.Exercises =
        [
            new ExerciseDraft
            {
                ExerciseType = null,
                Name = "Summary data (Autumn)",
                StartDate = new DateTime(2027, 1, 1),
                EndDate = new DateTime(2027, 1, 31)
            }
        ];
        ISession session = SessionWithDraft(draft);
        ExercisesController controller = Build(Substitute.For<IWindowService>(), session);

        controller.Submit(new ExercisesItem
        {
            Selected = ["Pupil data checking", "Results enquiry", "Summary data (Autumn)"]
        });

        CheckingWindowDraft saved = SavedDraft(session);
        Assert.Equal(new DateTime(2027, 1, 1),
            saved.Exercises.Single(e => e.Name == "Summary data (Autumn)").StartDate);
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

        Assert.True(model.All.Single(c => c.Name == "Pupil data checking").HasFiles);
    }

    [Fact]
    public async Task Edit_get_lists_a_hand_added_exercise_after_the_templates()
    {
        IWindowService service = Substitute.For<IWindowService>();
        CheckingWindowDto window = new()
        {
            Id = WindowId, Title = "w", KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
            StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 6, 1),
            Exercises =
            [
                new CheckingExerciseDto
                {
                    Id = Guid.NewGuid(), ExerciseType = CheckingExerciseType.PupilData, Name = "Pupil data checking",
                    StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 1, 14), SortOrder = 0
                },
                new CheckingExerciseDto
                {
                    Id = Guid.NewGuid(), ExerciseType = null, Name = "Retention",
                    StartDate = new DateTime(2027, 3, 1), EndDate = new DateTime(2027, 3, 31), SortOrder = 7
                }
            ]
        };
        service.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(window);
        ExercisesController controller = Build(service, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        ViewResult view = Assert.IsType<ViewResult>(await controller.Edit(WindowId, CancellationToken.None));
        ExercisesItem model = Assert.IsType<ExercisesItem>(view.Model);

        Assert.Equal(["Pupil data checking", "Results enquiry", "Summary data (Autumn)", "Retention"],
            model.All.Select(c => c.Name));
        Assert.Equal(["Pupil data checking", "Retention"], model.Selected);
    }

    [Fact]
    public async Task Update_post_redirects_to_summary_when_the_service_accepts_the_selection()
    {
        // The kept/added/dates/slots behaviour is WindowService.SetExercisesAsync's own — pinned in
        // WindowServiceExerciseTests — so here the controller only has to hand the tick list on and
        // act on the result.
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.SetExercisesAsync(WindowId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Ok());

        ExercisesController controller = Build(windowService, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        IActionResult result = await controller.Update(WindowId,
            new ExercisesItem { Selected = ["Pupil data checking", "Results enquiry"] }, CancellationToken.None);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ControllerName);
        await windowService.Received(1).SetExercisesAsync(WindowId,
            Arg.Is<IReadOnlyCollection<string>>(s => s.SequenceEqual(new[] { "Pupil data checking", "Results enquiry" })),
            Arg.Any<CancellationToken>());
        await windowService.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Update_post_maps_a_refusal_reason_onto_model_state_and_redisplays()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.SetExercisesAsync(WindowId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("Retention has change requests and cannot be removed"));
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(WindowWithFiles());

        ExercisesController controller = Build(windowService, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        IActionResult result = await controller.Update(WindowId,
            new ExercisesItem { Selected = ["Pupil data checking"] }, CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(result);
        Assert.IsType<ExercisesItem>(view.Model);
        Assert.True(controller.ModelState.ErrorCount > 0);
        Assert.Contains("Retention has change requests and cannot be removed",
            controller.ModelState[nameof(ExercisesItem.Selected)]!.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public async Task Update_post_returns_not_found_when_the_window_no_longer_exists()
    {
        IWindowService windowService = Substitute.For<IWindowService>();
        windowService.SetExercisesAsync(WindowId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ExerciseChangeResult.Refused("Window not found"));
        windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns((CheckingWindowDto?)null);

        ExercisesController controller = Build(windowService, Substitute.For<ISession>());
        controller.Url = _urlHelper;

        IActionResult result = await controller.Update(WindowId,
            new ExercisesItem { Selected = ["Pupil data checking"] }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
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
                Name = "Pupil data checking",
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
