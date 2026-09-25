using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #466 slice 3: the summary page is how an admin reaches any exercise, including a display-only
// data share, which Task 4 filtered off the page as an interim measure (it had no kind to build its
// links from). This is the test file that pins the filter's removal — recovered and adapted from
// the POC's CheckingExerciseSummaryControllerTests
// (git show GG/POC-ingressIntoCE:tests/.../CreateCheckingExerciseControllerTests.cs), which was
// dropped during Task 15 because the summary did not do any of this yet.
public sealed class SummaryControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Start = new(2026, 9, 1);
    private static readonly DateTime End = new(2026, 10, 1);

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();

    private SummaryController Controller() => new(_windowService, TimeProvider.System);

    private static WindowEditItem Model(IActionResult result) =>
        Assert.IsType<WindowEditItem>(Assert.IsType<ViewResult>(result).Model);

    private static CheckingWindowDto Window(params CheckingExerciseDto[] exercises) => Window(WindowId, exercises);

    private static CheckingWindowDto Window(Guid id, params CheckingExerciseDto[] exercises) => new()
    {
        Id = id, Title = "Post16 checking",
        StartDate = Start, EndDate = End,
        KeyStage = KeyStages.Post16, CheckingWindowType = CheckingWindowType.Post16,
        Exercises = exercises.ToList()
    };

    private static CheckingExerciseDto Exercise(
        string name, bool enabled = true, CheckingExerciseType? type = CheckingExerciseType.PupilData) => new()
    {
        Id = Guid.NewGuid(), Name = name, ExerciseType = type,
        StartDate = Start, EndDate = End, IsEnabled = enabled
    };

    [Fact]
    public async Task ListsEveryExerciseIncludingTheDisabledOnes()
    {
        // A disabled release is still history: its blobs and its change requests point at it.
        // The admin has to be able to see it, and to turn it back on.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(
            Exercise("Autumn release", enabled: true),
            Exercise("Withdrawn release", enabled: false)));

        var model = Model(await Controller().Index(WindowId, CancellationToken.None));

        Assert.Equal(["Autumn release", "Withdrawn release"], model.Exercises.Select(e => e.Name));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task IsPublishedOnlyWhileAnExerciseIsLive(bool enabled, bool published)
    {
        // No visibility dates, so the exercise is live exactly when it is enabled.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(
            Exercise("Autumn release", enabled: enabled)));

        var model = Model(await Controller().Index(WindowId, CancellationToken.None));

        Assert.Equal(published, model.IsPublished);
    }

    [Fact]
    public async Task NamesEachExerciseOnItsOwnEditLink()
    {
        // Every section has an Edit. Identical accessible names leave a screen reader user no way
        // to tell one release's Edit from the next.
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(
            Exercise("Autumn release"), Exercise("Revised release")));

        var model = Model(await Controller().Index(WindowId, CancellationToken.None));

        Assert.All(model.Exercises, exercise => Assert.NotEqual(Guid.Empty, exercise.Id));
        Assert.Equal(model.Exercises.Select(e => e.Id).Distinct().Count(), model.Exercises.Count);
    }

    [Fact]
    public async Task ADisplayOnlyExercise_AppearsOnTheSummary_WithIdAddressedLinks()
    {
        // Task 4's interim filter (Where(e => e.ExerciseType is not null)) is what this task
        // removes: a data share the admin created must appear on the summary of the window that
        // holds it, or there is no way to reach it again.
        var displayOnly = Exercise("Summary data", type: null);
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(displayOnly));

        var model = Model(await Controller().Index(WindowId, CancellationToken.None));

        var section = Assert.Single(model.Exercises);
        Assert.Equal("Summary data", section.Name);
        Assert.Equal("Data share", section.KindLabel);
        Assert.Equal(displayOnly.Id, section.Id);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{displayOnly.Id}/edit", section.EditLink);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{displayOnly.Id}/validate", section.ValidateLink);
    }

    [Fact]
    public async Task ValidateLink_IsAlwaysAddressedByExerciseId_EvenWhenTheExerciseHasAKind()
    {
        // Carried over from Slice 2: WindowViewModel.ValidateLink used to point at the kind route.
        // The kind route cannot serve a display-only exercise, so every section now uses the same
        // id-addressed route regardless of whether it has a kind.
        var exercise = Exercise("Provisional students");
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(exercise));

        var model = Model(await Controller().Index(WindowId, CancellationToken.None));

        var section = Assert.Single(model.Exercises);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{exercise.Id}/validate", section.ValidateLink);
    }

    [Fact]
    public async Task DatasetFileNames_AreExposedAsGivenByTheService_ViewPrintsOnlyTheBaseName()
    {
        // The view is responsible for Path.GetFileName; the model carries whatever the service
        // returned (see AccessibilityAuditViewTests.SummaryPage_ShowsDatasetFileNames_NotFullBlobPaths
        // for the markup-level pin of Path.GetFileName).
        var exercise = Exercise("Provisional students");
        exercise.Datasets =
        [
            new CheckingWindowDatasetDto
            {
                Name = "pupils", IngressFile = "ingress/abc/def/main.csv", SchemaFile = "schema/abc/def/main.json"
            }
        ];
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(exercise));

        var model = Model(await Controller().Index(WindowId, CancellationToken.None));

        var dataset = Assert.Single(Assert.Single(model.Exercises).Datasets);
        Assert.Equal("ingress/abc/def/main.csv", dataset.IngressFile);
        Assert.Equal("schema/abc/def/main.json", dataset.SchemaFile);
    }

    [Fact]
    public async Task Summary_ListsOnlyTheSelectedWindowsExercises_InOrder_IncludingRepeatedTypes()
    {
        var first = Window(Guid.NewGuid(), Exercise("Provisional students"));
        var second = Window(Guid.NewGuid(), Exercise("Provisional students"));
        first.Exercises.Add(new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), Name = "Revised students", TabName = "Students",
            IsEnabled = true,
            ExerciseType = CheckingExerciseType.PupilData, StartDate = first.StartDate, EndDate = first.EndDate,
            SortOrder = -1
        });
        _windowService.GetByIdAsync(first.Id, Arg.Any<CancellationToken>()).Returns(first);
        _windowService.GetByIdAsync(second.Id, Arg.Any<CancellationToken>()).Returns(second);

        var model = Model(await Controller().Index(first.Id, CancellationToken.None));

        Assert.Equal(new[] { "Revised students", "Provisional students" }, model.Exercises.Select(e => e.Name));
        Assert.All(model.Exercises, e => Assert.Equal(first.Id, e.WindowId));
        Assert.DoesNotContain(model.Exercises, e => e.Id == second.Exercises[0].Id);
        Assert.Equal("Students", model.Exercises[0].TabName);
        Assert.True(model.Exercises[0].IsEnabled);
        Assert.Contains(first.Exercises[1].Id.ToString(), model.Exercises[0].EditLink);
        Assert.Equal($"/admin/windows/{first.Id}/exercises/new", model.AddExerciseLink);
    }

    [Fact]
    public async Task Summary_HandlesEmptyAndMissingWindows()
    {
        var window = Window();
        _windowService.GetByIdAsync(window.Id, Arg.Any<CancellationToken>()).Returns(window);
        var controller = Controller();

        var model = Model(await controller.Index(window.Id, CancellationToken.None));

        Assert.Empty(model.Exercises);
        Assert.IsType<NotFoundResult>(await controller.Index(Guid.NewGuid(), CancellationToken.None));
    }
}
