using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Web;

public class AdminDashboardControllerTests
{
    private readonly IWindowService _windows = Substitute.For<IWindowService>();
    private readonly IDashboardService _dashboard = Substitute.For<IDashboardService>();

    private AdminDashboardController CreateSut()
        => new(_windows, _dashboard, Options.Create(new DashboardSettings { RefreshMinutes = 15 }));

    private static CheckingWindowDto Window(Guid id, string title, bool isOpen) => new()
    {
        Id = id,
        Title = title,
        IsOpen = isOpen,
        StartDate = new DateTime(2026, 06, 01),
        EndDate = new DateTime(2026, 06, 30),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
    };
    // As in DashboardServiceTests: satisfy any further required members with neutral values.
    // CAUTION: if CheckingWindowDto.IsOpen turns out to be computed from StartDate/EndDate
    // (get-only) rather than settable, drop the IsOpen initializer and instead pick
    // StartDate/EndDate values that straddle (open) or precede (closed) DateTime.UtcNow.

    private static DashboardMetrics Metrics(Guid windowId) => new()
    {
        WindowId = windowId, WindowTitle = "T",
        EligibleSchools = 1, LoggedIn = 1, NotLoggedIn = 0, SchoolsSubmitted = 0,
        LoggedInNotSubmitted = 1, TotalRequests = 0, AutoApproved = 0, AutoRejected = 0,
        RequiringScrutiny = 0, RefreshedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task Index_NoOpenWindows_ReturnsEmptyStateWithoutQueryingMetrics()
    {
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>())
            .Returns(new PageResult { Windows = [Window(Guid.NewGuid(), "Closed", isOpen: false)] });

        var result = Assert.IsType<ViewResult>(await CreateSut().Index(null, CancellationToken.None));

        var model = Assert.IsType<AdminDashboardViewModel>(result.Model);
        Assert.False(model.HasOpenWindows);
        Assert.Null(model.Metrics);
        await _dashboard.DidNotReceive().GetMetricsAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_DefaultsToFirstOpenWindow()
    {
        var open = Window(Guid.NewGuid(), "KS4 June 2026", isOpen: true);
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>())
            .Returns(new PageResult { Windows = [Window(Guid.NewGuid(), "Closed", false), open] });
        _dashboard.GetMetricsAsync(open, Arg.Any<CancellationToken>()).Returns(Metrics(open.Id));

        var result = Assert.IsType<ViewResult>(await CreateSut().Index(null, CancellationToken.None));

        var model = Assert.IsType<AdminDashboardViewModel>(result.Model);
        Assert.Equal(open.Id, model.SelectedWindowId);
        Assert.NotNull(model.Metrics);
        Assert.Single(model.OpenWindows);
    }

    [Fact]
    public async Task Index_SelectsRequestedOpenWindow()
    {
        var first = Window(Guid.NewGuid(), "KS4 June 2026", isOpen: true);
        var second = Window(Guid.NewGuid(), "Post 16 2026", isOpen: true);
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>())
            .Returns(new PageResult { Windows = [first, second] });
        _dashboard.GetMetricsAsync(second, Arg.Any<CancellationToken>()).Returns(Metrics(second.Id));

        var result = Assert.IsType<ViewResult>(await CreateSut().Index(second.Id, CancellationToken.None));

        var model = Assert.IsType<AdminDashboardViewModel>(result.Model);
        Assert.Equal(second.Id, model.SelectedWindowId);
    }

    [Fact]
    public async Task Index_UnknownWindowId_FallsBackToFirstOpenWindow()
    {
        var open = Window(Guid.NewGuid(), "KS4 June 2026", isOpen: true);
        _windows.GetAllDataAsync(Arg.Any<CancellationToken>())
            .Returns(new PageResult { Windows = [open] });
        _dashboard.GetMetricsAsync(open, Arg.Any<CancellationToken>()).Returns(Metrics(open.Id));

        var result = Assert.IsType<ViewResult>(await CreateSut().Index(Guid.NewGuid(), CancellationToken.None));

        var model = Assert.IsType<AdminDashboardViewModel>(result.Model);
        Assert.Equal(open.Id, model.SelectedWindowId);
    }
}
