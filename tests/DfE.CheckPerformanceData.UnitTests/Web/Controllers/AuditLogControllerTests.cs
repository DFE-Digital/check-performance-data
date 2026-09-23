using System.Reflection;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.AuditLog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The audit log (AB#294592) is two GETs over one query: parse the query string, one repository
// call, one view model (or one streamed CSV). Everything that decides what a row says is pinned here.
public sealed class AuditLogControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OlderWindowId = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly DateTime At = new(2026, 6, 8, 14, 38, 2, DateTimeKind.Utc);

    private readonly IAuditLogRepository _audit = Substitute.For<IAuditLogRepository>();
    private readonly IWindowService _windows = Substitute.For<IWindowService>();

    private AuditLogController Build(params AuditLogRow[] rows)
    {
        _audit.ListActivitiesAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "CheckingWindow", "EgressRun" });
        _audit.ListAsync(Arg.Any<AuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AuditLogPage(rows, rows.Length, 1, AuditLogController.PageSize));
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>()).Returns(new PageResult
        {
            Windows = [Window(OlderWindowId, "KS2 2025", CheckingWindowType.KS2, 2025), Window(WindowId, "KS4 June 2026", CheckingWindowType.KS4June, 2026)]
        });
        return new AuditLogController(_audit, _windows)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static CheckingWindowDto Window(Guid id, string title, CheckingWindowType type, int year) => new()
    {
        Id = id, Title = title, KeyStage = KeyStages.KS4, CheckingWindowType = type,
        StartDate = new DateTime(year, 6, 1), EndDate = new DateTime(year, 6, 30)
    };

    private static AuditLogRow Egress(AuditOutcome outcome, string? windowTitle = "KS4 June 2026") => new(
        1, At, "sub-1", "Ops One", "EgressRun", "33333333-3333-3333-3333-333333333333",
        outcome == AuditOutcome.Success ? "Transfer" : "TransferFailed", WindowId, windowTitle, ["NewLearners", "RemoveLearners"], outcome);

    private static AuditLogRow Generic() => new(2, At, "sub-2", null, "CheckingWindow", WindowId.ToString(), "Update", WindowId, "KS4 June 2026", [], null);

    private static AuditLogRow Plain() => new(3, At, "system", null, "Setting", "CMS:PageLength", "Update", null, null, [], null);

    // The generic capture's record of a run's creation (the pull): egress activity, no outcome.
    private static AuditLogRow Pulled() => new(4, At, "sub-1", null, "EgressRun", "44444444-4444-4444-4444-444444444444", "Insert", WindowId, "KS4 June 2026", [], null);

    private static async Task<AuditLogViewModel> ModelOf(AuditLogController controller, string? activity = null, Guid? windowId = null, string? status = null, int page = 1)
    {
        var view = Assert.IsType<ViewResult>(await controller.Index(activity, windowId, status, page, CancellationToken.None));
        return Assert.IsType<AuditLogViewModel>(view.Model);
    }

    [Fact]
    public void Controller_is_gated_by_its_own_section_at_class_level()
    {
        var gate = typeof(AuditLogController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.AuditLog, gate!.SectionKey);
        Assert.Equal("admin/audit-log", typeof(AuditLogController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public async Task Known_filters_pass_through_and_are_echoed_on_the_model()
    {
        var controller = Build();

        var model = await ModelOf(controller, "EgressRun", WindowId, "failed", page: 3);

        await _audit.Received(1).ListAsync(
            Arg.Is<AuditLogFilter>(f => f.Activity == "EgressRun" && f.WindowId == WindowId && f.Outcome == AuditOutcome.Failed),
            3, AuditLogController.PageSize, Arg.Any<CancellationToken>());
        Assert.Equal("EgressRun", model.SelectedActivity);
        Assert.Equal(WindowId, model.SelectedWindowId);
        Assert.Equal(AuditOutcome.Failed, model.SelectedOutcome);
        Assert.True(model.FiltersApplied);
    }

    [Fact]
    public async Task Unknown_activity_or_status_and_an_empty_guid_are_no_filter()
    {
        var controller = Build();

        var model = await ModelOf(controller, "Bogus", Guid.Empty, "nonsense");

        await _audit.Received(1).ListAsync(
            Arg.Is<AuditLogFilter>(f => f.Activity == null && f.WindowId == null && f.Outcome == null),
            1, AuditLogController.PageSize, Arg.Any<CancellationToken>());
        Assert.False(model.FiltersApplied);
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public async Task An_egress_row_shows_the_person_the_output_types_the_window_and_a_status_tag()
    {
        var model = await ModelOf(Build(Egress(AuditOutcome.Success)));

        var row = Assert.Single(model.Rows);
        Assert.Equal("Ops One", row.UserLabel);
        Assert.Equal("Data egress", row.ActivityLabel);
        Assert.Equal("govuk-tag--turquoise", row.ActivityTagClass);
        Assert.Null(row.ActivityDetail);
        Assert.Equal("KS4 June 2026", row.WindowTitle);
        Assert.Equal("New learners, Remove learners", row.WindowDetail);
        Assert.Equal(At, row.TimestampUtc);
        Assert.Equal("Success", row.OutcomeLabel);
        Assert.Equal("govuk-tag--green", row.OutcomeTagClass);
        Assert.Equal("EgressRun", row.EntityType);
        Assert.Equal("33333333-3333-3333-3333-333333333333", row.EntityId);
        Assert.Equal("Transfer", row.Action);
    }

    [Fact]
    public async Task A_failed_egress_row_is_red()
    {
        var row = Assert.Single((await ModelOf(Build(Egress(AuditOutcome.Failed)))).Rows);
        Assert.Equal("Failed", row.OutcomeLabel);
        Assert.Equal("govuk-tag--red", row.OutcomeTagClass);
    }

    [Fact]
    public async Task A_pull_is_egress_activity_with_no_status()
    {
        var row = Assert.Single((await ModelOf(Build(Pulled()))).Rows);
        Assert.Equal("sub-1", row.UserLabel);               // the generic capture knows the subject id only
        Assert.Equal("Data egress", row.ActivityLabel);
        Assert.Equal("govuk-tag--turquoise", row.ActivityTagClass);
        Assert.Equal("Run started", row.ActivityDetail);
        Assert.Equal("KS4 June 2026", row.WindowTitle);
        Assert.Null(row.WindowDetail);
        Assert.Null(row.OutcomeLabel);
        Assert.Null(row.OutcomeTagClass);
        Assert.Equal("Insert", row.Action);
    }

    [Fact]
    public async Task An_egress_row_whose_window_cannot_be_named_says_so()
    {
        var row = Assert.Single((await ModelOf(Build(Egress(AuditOutcome.Success, windowTitle: null)))).Rows);
        Assert.Equal(AuditLogRowViewModel.UnknownWindow, row.WindowTitle);
        Assert.Equal("Unknown window", row.WindowTitle);
    }

    [Fact]
    public async Task A_generic_row_shows_the_subject_id_the_action_and_no_status()
    {
        var model = await ModelOf(Build(Generic(), Plain()));

        var window = model.Rows[0];
        Assert.Equal("sub-2", window.UserLabel);
        Assert.Equal("Checking window", window.ActivityLabel);
        Assert.Equal("govuk-tag--grey", window.ActivityTagClass);
        Assert.Equal("Update", window.ActivityDetail);
        Assert.Equal("KS4 June 2026", window.WindowTitle);
        Assert.Null(window.WindowDetail);
        Assert.Null(window.OutcomeLabel);
        Assert.Null(window.OutcomeTagClass);

        var plain = model.Rows[1];
        Assert.Equal("system", plain.UserLabel);
        Assert.Equal("System setting", plain.ActivityLabel);
        Assert.Equal(string.Empty, plain.WindowTitle);   // no window at all: nothing, not "Unknown window"
    }

    [Fact]
    public async Task Options_are_labelled_and_windows_read_as_the_egress_pages_label_them()
    {
        var model = await ModelOf(Build());

        Assert.Equal(new[] { ("CheckingWindow", "Checking window"), ("EgressRun", "Data egress") }, model.Activities.Select(a => (a.Value, a.Label)));
        Assert.Equal(new[] { "KS4 June 2026 (KS4)", "KS2 2025 (KS2)" }, model.Windows.Select(w => w.Label));   // newest start first
        Assert.Equal(new[] { WindowId, OlderWindowId }, model.Windows.Select(w => w.Id));
    }

    [Fact]
    public async Task Export_and_page_links_carry_every_filter_and_nothing_else()
    {
        var model = await ModelOf(Build(), "EgressRun", WindowId, "Success");
        Assert.Equal($"/admin/audit-log/export?activity=EgressRun&windowId={WindowId}&status=Success", model.ExportUrl);
        Assert.Equal($"/admin/audit-log?activity=EgressRun&windowId={WindowId}&status=Success&page=2", model.PageUrl(2));

        var bare = await ModelOf(Build());
        Assert.Equal("/admin/audit-log/export", bare.ExportUrl);
        Assert.Equal("/admin/audit-log?page=2", bare.PageUrl(2));
    }

    [Fact]
    public async Task Export_streams_csv_under_a_dated_file_name_with_the_same_filters()
    {
        var controller = Build();

        var result = Assert.IsType<AuditLogCsvResult>(await controller.Export("EgressRun", WindowId, "Success", CancellationToken.None));

        _audit.Received(1).StreamAsync(
            Arg.Is<AuditLogFilter>(f => f.Activity == "EgressRun" && f.WindowId == WindowId && f.Outcome == AuditOutcome.Success),
            Arg.Any<CancellationToken>());
        Assert.StartsWith("audit-log-", result.FileName);
        Assert.EndsWith(".csv", result.FileName);
    }
}
