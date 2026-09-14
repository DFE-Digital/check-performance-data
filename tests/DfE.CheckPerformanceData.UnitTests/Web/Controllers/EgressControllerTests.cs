using System.Reflection;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.Egress;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class EgressControllerTests
{
    private static readonly Guid RunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly IEgressRunService _runs = Substitute.For<IEgressRunService>();
    private readonly IEgressPreprocessor _preprocessor = Substitute.For<IEgressPreprocessor>();
    private readonly IEgressTransferService _transfer = Substitute.For<IEgressTransferService>();
    private readonly IEgressBlobClient _blobs = Substitute.For<IEgressBlobClient>();
    private readonly IWindowService _windows = Substitute.For<IWindowService>();
    private readonly ICurrentUserService _user = Substitute.For<ICurrentUserService>();

    private EgressController Build()
    {
        _user.UserId.Returns("22222222-2222-2222-2222-222222222222");
        _user.DisplayName.Returns("Ops One");
        _user.Email.Returns("ops@example.com");
        _blobs.TargetDescription.Returns("cypmd/extracts_input");
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult { Windows = [Window()] });
        _runs.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        var controller = new EgressController(_runs, _preprocessor, _transfer, _blobs, _windows, _user)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static CheckingWindowDto Window() => new()
    {
        Id = WindowId, Title = "KS4 June 2026", KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
        StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
    };

    private static EgressRunDto Run(EgressRunStatus status) => new(RunId, WindowId, status, Guid.NewGuid(), "Ops One", DateTime.UtcNow,
        null, null, null, null, [], null, [new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, [], 0, null, null, null)]);

    [Fact]
    public void Controller_is_gated_by_the_egress_section()
    {
        var gate = typeof(EgressController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.Egress, gate!.SectionKey);
    }

    [Fact]
    public async Task Start_with_nothing_selected_redisplays_with_errors()
    {
        var result = await Build().Start(new PullForm { WindowId = null, OutputTypes = [] }, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<PullViewModel>(view.Model);
        Assert.False(model.IsValid);
        await _runs.DidNotReceiveWithAnyArgs().StartAsync(default, default!, default!, default);
    }

    // S9: an unbindable OutputTypes value (e.g. OutputTypes=garbage) previously bound as
    // default(EgressOutputType) — NewLearners — with nobody reading the resulting ModelState
    // error, so the request silently proceeded. A window is selected here so only the
    // OutputTypes binding failure is under test.
    [Fact]
    public async Task Start_with_an_unbindable_output_type_redisplays_with_an_error_rather_than_defaulting()
    {
        var controller = Build();
        controller.ModelState.AddModelError(nameof(PullForm.OutputTypes), "The value 'garbage' is not valid.");

        var result = await controller.Start(
            new PullForm { WindowId = WindowId, OutputTypes = [EgressOutputType.NewLearners] }, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        var model = Assert.IsType<PullViewModel>(view.Model);
        Assert.NotNull(model.OutputTypesError);
        await _runs.DidNotReceiveWithAnyArgs().StartAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Start_redirects_to_results_and_passes_the_actor()
    {
        EgressActor? actor = null;
        _runs.StartAsync(WindowId, Arg.Any<IReadOnlyList<EgressOutputType>>(), Arg.Do<EgressActor>(a => actor = a), Arg.Any<CancellationToken>())
            .Returns(new EgressStartResult.Started(RunId));

        var result = await Build().Start(new PullForm { WindowId = WindowId, OutputTypes = [EgressOutputType.RemoveLearners] }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(EgressController.Results), redirect.ActionName);
        Assert.Equal(RunId, redirect.RouteValues!["id"]);
        Assert.Equal("Ops One", actor!.DisplayName);
    }

    [Fact]
    public async Task Start_shows_who_holds_the_pair_when_refused()
    {
        var blocker = new EgressBlocker(Guid.NewGuid(), EgressRunStatus.Transferred, "Ops Two", new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 8, 14, 38, 0, DateTimeKind.Utc), "Ops Two");
        _runs.StartAsync(WindowId, Arg.Any<IReadOnlyList<EgressOutputType>>(), Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressStartResult.Refused([(EgressOutputType.RemoveLearners, blocker)]));

        var view = Assert.IsType<ViewResult>(await Build().Start(new PullForm { WindowId = WindowId, OutputTypes = [EgressOutputType.RemoveLearners] }, CancellationToken.None));

        var model = Assert.IsType<PullViewModel>(view.Model);
        var refusal = Assert.Single(model.Refusals);
        Assert.Contains("Remove learners", refusal);
        Assert.Contains("Ops Two", refusal);
        Assert.Contains("already been transferred", refusal);
    }

    [Theory]
    [InlineData(EgressRunStatus.Pulled, nameof(EgressController.Results))]
    [InlineData(EgressRunStatus.Preprocessing, nameof(EgressController.Preprocessing))]
    [InlineData(EgressRunStatus.PreprocessingFailed, nameof(EgressController.Failed))]
    [InlineData(EgressRunStatus.Preprocessed, nameof(EgressController.Summary))]
    [InlineData(EgressRunStatus.TransferFailed, nameof(EgressController.Summary))]
    [InlineData(EgressRunStatus.Transferring, nameof(EgressController.Summary))]
    [InlineData(EgressRunStatus.Transferred, nameof(EgressController.Complete))]
    public async Task Resume_opens_the_page_the_status_implies(EgressRunStatus status, string action)
    {
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(status));
        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Resume(RunId, CancellationToken.None));
        Assert.Equal(action, redirect.ActionName);
    }

    [Fact]
    public async Task Resume_of_an_abandoned_or_unknown_run_goes_home_with_a_message()
    {
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(EgressRunStatus.Abandoned));
        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Resume(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Index), redirect.ActionName);

        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns((EgressRunDto?)null);
        Assert.IsType<NotFoundResult>(await Build().Resume(RunId, CancellationToken.None));
    }

    [Theory]
    [InlineData(EgressRunStatus.Pulled)]
    [InlineData(EgressRunStatus.Abandoned)]
    public async Task Summary_refuses_a_run_that_is_not_ready(EgressRunStatus status)
    {
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(status));
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Summary(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Resume), redirect.ActionName);
    }

    // S10: Start's other two non-happy outcomes had no coverage.
    [Fact]
    public async Task Start_shows_the_window_error_when_the_window_no_longer_exists()
    {
        _runs.StartAsync(WindowId, Arg.Any<IReadOnlyList<EgressOutputType>>(), Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressStartResult.WindowNotFound());

        var view = Assert.IsType<ViewResult>(await Build().Start(new PullForm { WindowId = WindowId, OutputTypes = [EgressOutputType.RemoveLearners] }, CancellationToken.None));

        var model = Assert.IsType<PullViewModel>(view.Model);
        Assert.Equal("Select a checking window", model.WindowError);
    }

    [Fact]
    public async Task Start_shows_the_pull_error_when_zendesk_could_not_be_read()
    {
        _runs.StartAsync(WindowId, Arg.Any<IReadOnlyList<EgressOutputType>>(), Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressStartResult.PullFailed("Zendesk timed out"));

        var view = Assert.IsType<ViewResult>(await Build().Start(new PullForm { WindowId = WindowId, OutputTypes = [EgressOutputType.RemoveLearners] }, CancellationToken.None));

        var model = Assert.IsType<PullViewModel>(view.Model);
        Assert.Equal("Zendesk timed out", model.PullError);
    }

    // S10: Results/Failed/Complete each guard their status but had no test.
    [Theory]
    [InlineData(EgressRunStatus.Pulled)]
    [InlineData(EgressRunStatus.PreprocessingFailed)]
    [InlineData(EgressRunStatus.Preprocessed)]
    public async Task Results_renders_for_the_statuses_it_allows(EgressRunStatus status)
    {
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(status));
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        var view = Assert.IsType<ViewResult>(await Build().Results(RunId, CancellationToken.None));
        Assert.Equal("Results", view.ViewName);
    }

    [Fact]
    public async Task Results_redirects_to_resume_for_a_status_it_does_not_allow()
    {
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(EgressRunStatus.Transferring));
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Results(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Resume), redirect.ActionName);
    }

    // Nit: a Transferred/Abandoned run has nothing left to preprocess.
    [Theory]
    [InlineData(EgressRunStatus.Transferred)]
    [InlineData(EgressRunStatus.Abandoned)]
    public async Task Preprocessing_redirects_to_resume_for_a_finished_run(EgressRunStatus status)
    {
        var run = Run(status) with { Outputs = [new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, [], 3, null, null, null)] };
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());

        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Preprocessing(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Resume), redirect.ActionName);
    }

    [Fact]
    public async Task Failed_renders_only_for_preprocessing_failed()
    {
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(EgressRunStatus.PreprocessingFailed));
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        var view = Assert.IsType<ViewResult>(await Build().Failed(RunId, CancellationToken.None));
        Assert.Equal("Failed", view.ViewName);

        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(EgressRunStatus.Pulled));
        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Failed(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Resume), redirect.ActionName);
    }

    [Fact]
    public async Task Complete_renders_only_for_transferred()
    {
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(EgressRunStatus.Transferred));
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        var view = Assert.IsType<ViewResult>(await Build().Complete(RunId, CancellationToken.None));
        Assert.Equal("Complete", view.ViewName);

        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(Run(EgressRunStatus.Preprocessed));
        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Complete(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Resume), redirect.ActionName);
    }

    // S10: no PreprocessingRun (the no-JS fallback) outcome was covered.
    private static async IAsyncEnumerable<EgressProgress> One(EgressProgress p) { await Task.Yield(); yield return p; }

    [Fact]
    public async Task PreprocessingRun_redirects_to_summary_when_the_pipeline_completes()
    {
        _preprocessor.RunAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(One(new EgressProgress(8, 8, "Save to database", "done", 1, 1, 0, true, false, "Saved 1 record(s).", EgressRunStatus.Preprocessed)));

        var redirect = Assert.IsType<RedirectToActionResult>(await Build().PreprocessingRun(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Summary), redirect.ActionName);
    }

    [Fact]
    public async Task PreprocessingRun_redirects_to_failed_when_the_pipeline_fails()
    {
        _preprocessor.RunAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(One(new EgressProgress(8, 8, "Save to database", "failed", 1, 0, 1, true, true, "Preprocessing stopped.", EgressRunStatus.PreprocessingFailed)));

        var redirect = Assert.IsType<RedirectToActionResult>(await Build().PreprocessingRun(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Failed), redirect.ActionName);
    }

    [Fact]
    public async Task PreprocessingRun_goes_home_with_the_message_when_it_does_not_complete()
    {
        _preprocessor.RunAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(One(new EgressProgress(0, 8, "Preprocessing", "failed", 0, 0, 0, true, true, "This run is Transferring and cannot be preprocessed.", null)));

        var controller = Build();
        var redirect = Assert.IsType<RedirectToActionResult>(await controller.PreprocessingRun(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Index), redirect.ActionName);
        Assert.Equal("This run is Transferring and cannot be preprocessed.", controller.TempData[EgressController.BannerKey]);
    }

    // S10/Nit: Preview must come from the saved rows via EgressColumnSets, not by splitting the
    // CSV text — a quoted value containing a comma (a real surname, e.g. "Smith, Jr") would
    // otherwise shift every column after it.
    [Fact]
    public async Task Preview_builds_the_table_from_the_saved_rows_not_the_csv_text()
    {
        var run = Run(EgressRunStatus.Preprocessed) with
        {
            Outputs = [new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, [], 2, 2, "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", null)]
        };
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _transfer.GetPreviewAsync(RunId, EgressOutputType.RemoveLearners, Arg.Any<CancellationToken>())
            .Returns((EgressColumnSets.RemoveLearners.Select(c => c.Header).ToList(),
                (IReadOnlyList<IReadOnlyList<string>>)[["1001", "31", "4", "KS4", "4070", "Smith, Jr", "Alice", "F", "2010-09-07", "2026", "6", "860", "555"]]));

        var view = Assert.IsType<ViewResult>(await Build().Preview(RunId, EgressOutputType.RemoveLearners, CancellationToken.None));

        Assert.Equal("Preview", view.ViewName);
        var model = Assert.IsType<PreviewViewModel>(view.Model);
        Assert.Equal(EgressColumnSets.RemoveLearners.Select(c => c.Header), model.Headers);
        var row = Assert.Single(model.Rows);
        Assert.Equal("Smith, Jr", row[5]);
        Assert.Equal(13, row.Count);
        await _transfer.DidNotReceiveWithAnyArgs().BuildFileAsync(default, default, default);
    }

    [Fact]
    public async Task Preview_404s_when_the_run_has_not_reached_this_output_yet()
    {
        var run = Run(EgressRunStatus.Pulled);
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);

        Assert.IsType<NotFoundResult>(await Build().Preview(RunId, EgressOutputType.RemoveLearners, CancellationToken.None));
    }

    // S10: only the success/Failed branches of Transfer were covered; Refused was not.
    // Nit: EgressTransferResult.Refused now carries the specific output type instead of the
    // controller hard-coding RemoveLearners and string-replacing the description — NewLearners
    // here proves the real type is used, not the old placeholder.
    [Fact]
    public async Task Transfer_refused_shows_who_holds_the_pair_and_returns_to_summary()
    {
        var blocker = new EgressBlocker(Guid.NewGuid(), EgressRunStatus.Pulled, "Ops Two", DateTime.UtcNow, null, null);
        _transfer.TransferAsync(RunId, Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressTransferResult.Refused(EgressOutputType.NewLearners, blocker));

        var controller = Build();
        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Transfer(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Summary), redirect.ActionName);
        var message = Assert.IsType<string>(controller.TempData[EgressController.TransferErrorKey]);
        Assert.Contains("Ops Two", message);
        Assert.Contains("New learners", message);
    }

    [Fact]
    public async Task Transfer_of_an_unknown_run_returns_not_found()
    {
        _transfer.TransferAsync(RunId, Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressTransferResult.NotFound());

        Assert.IsType<NotFoundResult>(await Build().Transfer(RunId, CancellationToken.None));
    }

    [Fact]
    public async Task Transfer_success_redirects_to_complete_and_failure_back_to_summary_with_the_reason()
    {
        _transfer.TransferAsync(RunId, Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressTransferResult.Transferred([(EgressOutputType.RemoveLearners, "f.csv", 2)], DateTime.UtcNow));
        var ok = Assert.IsType<RedirectToActionResult>(await Build().Transfer(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Complete), ok.ActionName);

        _transfer.TransferAsync(RunId, Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressTransferResult.Failed("Blob upload refused"));
        var controller = Build();
        var back = Assert.IsType<RedirectToActionResult>(await controller.Transfer(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Summary), back.ActionName);
        Assert.Equal("Blob upload refused", controller.TempData[EgressController.TransferErrorKey]);
    }

    // M3: never Complete for an empty approved set.
    [Fact]
    public async Task Transfer_of_an_empty_approved_set_redirects_to_summary_not_complete()
    {
        _transfer.TransferAsync(RunId, Arg.Any<EgressActor>(), Arg.Any<CancellationToken>())
            .Returns(new EgressTransferResult.NothingToTransfer());

        var redirect = Assert.IsType<RedirectToActionResult>(await Build().Transfer(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Summary), redirect.ActionName);
    }

    [Fact]
    public async Task Download_returns_csv_with_the_runs_file_name()
    {
        var run = Run(EgressRunStatus.Preprocessed) with
        {
            Outputs = [new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, [], 2, 2, "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", null)]
        };
        _runs.GetAsync(RunId, Arg.Any<CancellationToken>()).Returns(run);
        _transfer.BuildFileAsync(RunId, EgressOutputType.RemoveLearners, Arg.Any<CancellationToken>()).Returns([65, 44, 66]);

        var file = Assert.IsType<FileContentResult>(await Build().Download(RunId, EgressOutputType.RemoveLearners, CancellationToken.None));

        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", file.FileDownloadName);
    }

    // S10: rewritten to assert the banner text the outcome actually produces, not merely that the
    // mock was invoked — the four EgressAbandonResult branches each need a distinct message.
    [Fact]
    public async Task Abandon_of_an_untransferred_run_reports_nothing_was_transferred()
    {
        _transfer.AbandonAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressAbandonResult.Abandoned([]));
        var controller = Build();

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Abandon(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Index), redirect.ActionName);
        Assert.Equal("The egress run was abandoned. Nothing was transferred.", controller.TempData[EgressController.BannerKey]);
    }

    [Fact]
    public async Task Abandon_of_a_run_that_swept_blobs_names_what_was_removed()
    {
        _transfer.AbandonAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(new EgressAbandonResult.Abandoned(["CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv"]));
        var controller = Build();

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Abandon(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Index), redirect.ActionName);
        var banner = Assert.IsType<string>(controller.TempData[EgressController.BannerKey]);
        Assert.Contains("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", banner);
        Assert.DoesNotContain("Nothing was transferred", banner);
    }

    [Fact]
    public async Task Abandon_of_an_already_transferred_run_refuses_without_re_abandoning()
    {
        _transfer.AbandonAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressAbandonResult.AlreadyTransferred());
        var controller = Build();

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Abandon(RunId, CancellationToken.None));

        Assert.Equal(nameof(EgressController.Index), redirect.ActionName);
        var banner = Assert.IsType<string>(controller.TempData[EgressController.BannerKey]);
        Assert.Contains("already been transferred", banner);
    }

    [Fact]
    public async Task Abandon_of_an_unknown_run_returns_not_found()
    {
        _transfer.AbandonAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressAbandonResult.NotFound());

        Assert.IsType<NotFoundResult>(await Build().Abandon(RunId, CancellationToken.None));
    }
}
