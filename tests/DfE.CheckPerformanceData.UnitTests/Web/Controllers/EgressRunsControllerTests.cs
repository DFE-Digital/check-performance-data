using System.Reflection;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.Egress;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The runs history (AB#294590) is a read-only GET: query-string parsing, one service call, one
// view model. Everything that decides what a row says or links to is pinned here.
public sealed class EgressRunsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OlderWindowId = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly Guid RunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime Started = new(2026, 6, 8, 14, 38, 0, DateTimeKind.Utc);

    private readonly IEgressRunService _runs = Substitute.For<IEgressRunService>();
    private readonly IWindowService _windows = Substitute.For<IWindowService>();

    private EgressRunsController Build(EgressRunHistoryPage? page = null)
    {
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult { Windows = [Window(OlderWindowId, "KS2 2025", CheckingWindowType.KS2, 2025), Window(WindowId, "KS4 June 2026", CheckingWindowType.KS4June, 2026)] });
        _runs.ListHistoryAsync(Arg.Any<EgressRunHistoryFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(page ?? new EgressRunHistoryPage([], 0, 1, EgressRunsController.PageSize));
        return new EgressRunsController(_runs, _windows)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static CheckingWindowDto Window(Guid id, string title, CheckingWindowType type, int year) => new()
    {
        Id = id, Title = title, KeyStage = KeyStages.KS4, CheckingWindowType = type,   // KeyStage is not read by the page
        StartDate = new DateTime(year, 6, 1), EndDate = new DateTime(year, 6, 30)
    };

    private static EgressRunHistoryRow Row(EgressRunStatus status, int records = 0, params EgressOutputType[] types) =>
        new(RunId, WindowId, "KS4 June 2026", status, types.Length == 0 ? [EgressOutputType.RemoveLearners] : types, records, "Ops One", Started);

    private static async Task<EgressRunsViewModel> ModelOf(EgressRunsController controller, Guid? windowId = null, string? status = null, int page = 1)
    {
        var view = Assert.IsType<ViewResult>(await controller.Index(windowId, status, page, CancellationToken.None));
        return Assert.IsType<EgressRunsViewModel>(view.Model);
    }

    [Fact]
    public void Controller_is_gated_by_its_own_section_at_class_level()
    {
        var gate = typeof(EgressRunsController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.EgressRuns, gate!.SectionKey);
        Assert.Equal("admin/egress/runs", typeof(EgressRunsController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public async Task With_no_query_the_service_is_asked_for_page_one_of_everything()
    {
        var model = await ModelOf(Build());

        await _runs.Received(1).ListHistoryAsync(new EgressRunHistoryFilter(null, null), 1, EgressRunsController.PageSize, Arg.Any<CancellationToken>());
        Assert.Equal(20, EgressRunsController.PageSize);
        Assert.Null(model.SelectedWindowId);
        Assert.Null(model.SelectedOutcome);
        Assert.False(model.FiltersApplied);
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public async Task Window_status_and_page_are_passed_through_and_echoed()
    {
        var model = await ModelOf(Build(new EgressRunHistoryPage([], 45, 3, 20)), WindowId, "failed", 3);

        await _runs.Received(1).ListHistoryAsync(new EgressRunHistoryFilter(WindowId, EgressRunOutcome.Failed), 3, 20, Arg.Any<CancellationToken>());
        Assert.Equal(WindowId, model.SelectedWindowId);
        Assert.Equal(EgressRunOutcome.Failed, model.SelectedOutcome);
        Assert.True(model.FiltersApplied);
        Assert.Equal(3, model.Page);
        Assert.Equal(3, model.TotalPages);
        Assert.Equal(45, model.TotalCount);
    }

    [Fact]
    public async Task An_unknown_status_and_an_empty_window_are_no_filter()
    {
        var model = await ModelOf(Build(), Guid.Empty, "bogus");

        await _runs.Received(1).ListHistoryAsync(new EgressRunHistoryFilter(null, null), 1, 20, Arg.Any<CancellationToken>());
        Assert.False(model.FiltersApplied);
    }

    // The page echoes what the repository clamped to, not what the URL said.
    [Fact]
    public async Task The_model_reports_the_clamped_page_the_service_returned()
    {
        var model = await ModelOf(Build(new EgressRunHistoryPage([Row(EgressRunStatus.Abandoned)], 21, 2, 20)), page: 99);

        Assert.Equal(2, model.Page);
        Assert.Equal(2, model.TotalPages);
    }

    // The select must read exactly as the Pull page's select does: newest window first, the
    // stage token in brackets.
    [Fact]
    public async Task Window_choices_are_labelled_and_ordered_as_the_pull_page_labels_them()
    {
        var model = await ModelOf(Build());

        Assert.Equal(["KS4 June 2026 (KS4)", "KS2 2025 (KS2)"], model.Windows.Select(w => w.Label));
        Assert.Equal([WindowId, OlderWindowId], model.Windows.Select(w => w.Id));
    }

    [Theory]
    [InlineData(EgressRunStatus.Transferred, EgressRunOutcome.Success, "Success", "govuk-tag--green", "View")]
    [InlineData(EgressRunStatus.PreprocessingFailed, EgressRunOutcome.Failed, "Failed", "govuk-tag--red", "View")]
    [InlineData(EgressRunStatus.TransferFailed, EgressRunOutcome.Failed, "Failed", "govuk-tag--red", "View")]
    [InlineData(EgressRunStatus.Pulled, EgressRunOutcome.Draft, "Draft", "govuk-tag--blue", "Resume")]
    [InlineData(EgressRunStatus.Transferring, EgressRunOutcome.Draft, "Draft", "govuk-tag--blue", "Resume")]
    [InlineData(EgressRunStatus.Abandoned, EgressRunOutcome.Abandoned, "Abandoned", "govuk-tag--grey", null)]
    public async Task Each_row_carries_its_outcome_label_tag_and_link(EgressRunStatus status, EgressRunOutcome outcome, string label, string tag, string? link)
    {
        var model = await ModelOf(Build(new EgressRunHistoryPage([Row(status, records: 7)], 1, 1, 20)));

        var row = Assert.Single(model.Rows);
        Assert.Equal(RunId, row.Id);
        Assert.Equal(outcome, row.Outcome);
        Assert.Equal(label, row.OutcomeLabel);
        Assert.Equal(tag, row.OutcomeTagClass);
        Assert.Equal(link, row.LinkText);
        Assert.Equal(link == "Resume", row.CanResume);
        Assert.Equal(link == "View", row.CanView);
        Assert.Equal("KS4 June 2026", row.WindowTitle);
        Assert.Equal("Ops One", row.StartedByName);
        Assert.Equal(Started, row.StartedAtUtc);
        Assert.Equal(7, row.RecordsTransferred);   // the repository already zeroed non-success rows; the controller does not second-guess it
    }

    [Fact]
    public async Task Every_output_type_in_a_run_is_labelled()
    {
        var model = await ModelOf(Build(new EgressRunHistoryPage([Row(EgressRunStatus.Transferred, 60, EgressOutputType.NewLearners, EgressOutputType.RemoveLearners)], 1, 1, 20)));

        Assert.Equal(["New learners", "Remove learners"], Assert.Single(model.Rows).OutputTypeLabels);
    }

    [Fact]
    public async Task A_missing_window_list_is_an_empty_select_not_an_error()
    {
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns((PageResult?)null);
        var controller = new EgressRunsController(_runs, _windows) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        _runs.ListHistoryAsync(Arg.Any<EgressRunHistoryFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new EgressRunHistoryPage([], 0, 1, 20));

        var model = await ModelOf(controller);

        Assert.Empty(model.Windows);
    }
}
