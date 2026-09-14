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

        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", file.FileDownloadName);
    }

    // M1: moved from IEgressRunService to IEgressTransferService, since Abandon now needs blob
    // access to sweep a Transferring run's own files. Fuller state/banner coverage is S10.
    [Fact]
    public async Task Abandon_marks_the_run_and_returns_home()
    {
        _transfer.AbandonAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressAbandonResult.Abandoned([]));
        var controller = Build();
        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Abandon(RunId, CancellationToken.None));
        Assert.Equal(nameof(EgressController.Index), redirect.ActionName);
        await _transfer.Received(1).AbandonAsync(RunId, Arg.Any<CancellationToken>());
        Assert.NotNull(controller.TempData[EgressController.BannerKey]);
    }
}
