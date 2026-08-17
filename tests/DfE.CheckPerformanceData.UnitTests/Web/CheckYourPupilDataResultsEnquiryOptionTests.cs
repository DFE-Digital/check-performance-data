using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// AB#296648: the way in to the enquiry journey.
//
// Per the agreed decision in docs/16-19-window-model.md, the check-your-pupil-data page's existing
// "what would you like to do?" radios gain a third option for 16-19 windows. Enquiries are 16-19 only
// in this ticket, so a KS4 window must neither show the option nor accept it if posted.
public sealed class CheckYourPupilDataResultsEnquiryOptionTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly FakeSession _session = new();
    private readonly CheckYourPupilDataController _sut;

    public CheckYourPupilDataResultsEnquiryOptionTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _service.GetPupilTableAsync(WindowId, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns((PupilTable.Empty, 0));

        _sut = new CheckYourPupilDataController(_service, TimeProvider.System, _currentUser, _analytics)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private void Window(CheckingWindowType type, bool open = true)
    {
        var now = DateTime.UtcNow;
        _service.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Title = type == CheckingWindowType.Post16 ? "16 to 19 2026" : "KS4 June 2026",
            KeyStage = type == CheckingWindowType.Post16 ? KeyStages.Post16 : KeyStages.KS4,
            CheckingWindowType = type,
            StartDate = open ? now.AddDays(-5) : now.AddDays(-40),
            EndDate = open ? now.AddDays(5) : now.AddDays(-10)
        });
    }

    private async Task<CheckYourPupilDataViewModel> IndexModel()
    {
        var view = Assert.IsType<ViewResult>(await _sut.Index(WindowId));
        return Assert.IsType<CheckYourPupilDataViewModel>(view.Model);
    }

    // ── Visibility ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_16_to_19_window_offers_the_results_enquiry_option()
    {
        Window(CheckingWindowType.Post16);

        Assert.True((await IndexModel()).ShowResultsEnquiryOption);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public async Task Other_window_types_do_not_offer_it(CheckingWindowType type)
    {
        // Enquiries are 16-19 only in this ticket; the other key stages have no results data and no
        // flow config, so the option would dead-end.
        Window(type);

        Assert.False((await IndexModel()).ShowResultsEnquiryOption);
    }

    [Fact]
    public async Task A_closed_window_offers_nothing_at_all()
    {
        // The whole radio group is already hidden when the window is shut; the new option must not
        // reintroduce a way in. PARKED: per-activity visibility replaces this when the window-model
        // activity dates land.
        Window(CheckingWindowType.Post16, open: false);

        var model = await IndexModel();
        Assert.False(model.IsWindowOpen);
    }

    // ── Routing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Choosing_it_routes_to_the_result_issue_page()
    {
        Window(CheckingWindowType.Post16);

        var result = await _sut.NextStep(WindowId, new CheckYourPupilDataViewModel
        {
            WindowId = WindowId.ToString(),
            SelectedNextStep = NextSteps.ResultsEnquiry,
            WindowEndDate = "", WindowEndTime = "", WindowTitle = "", Sections = [],
            SectionsAsTabs = false, IsWindowOpen = true, OrganisationName = ""
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ResultIssue", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
    }

    [Fact]
    public async Task The_existing_options_still_route_where_they_did()
    {
        Window(CheckingWindowType.Post16);

        CheckYourPupilDataViewModel Vm(NextSteps step) => new()
        {
            WindowId = WindowId.ToString(),
            SelectedNextStep = step,
            WindowEndDate = "", WindowEndTime = "", WindowTitle = "", Sections = [],
            SectionsAsTabs = false, IsWindowOpen = true, OrganisationName = ""
        };

        var change = Assert.IsType<RedirectToActionResult>(await _sut.NextStep(WindowId, Vm(NextSteps.RequestChange)));
        Assert.Equal("WhatToChange", change.ControllerName);

        var confirm = Assert.IsType<RedirectToActionResult>(await _sut.NextStep(WindowId, Vm(NextSteps.Confirm)));
        Assert.Equal("ConfirmCorrect", confirm.ControllerName);
    }

    // ── The KS4 exclusion is enforced server-side ────────────────────────────

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public async Task Posting_it_on_a_window_that_does_not_offer_it_is_rejected_as_unanswered(
        CheckingWindowType type)
    {
        // Not rendering the option is not enough — a hand-crafted post must not start a journey that
        // has no results data behind it. Same fail-closed rule as the hidden-radio rejection.
        Window(type);

        var result = await _sut.NextStep(WindowId, new CheckYourPupilDataViewModel
        {
            WindowId = WindowId.ToString(),
            SelectedNextStep = NextSteps.ResultsEnquiry,
            WindowEndDate = "", WindowEndTime = "", WindowTitle = "", Sections = [],
            SectionsAsTabs = false, IsWindowOpen = true, OrganisationName = ""
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        Assert.Equal(
            "Select what you would like to do",
            _sut.ModelState[nameof(CheckYourPupilDataViewModel.SelectedNextStep)]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task A_rejected_post_does_not_record_the_choice_in_session()
    {
        Window(CheckingWindowType.KS4June);

        await _sut.NextStep(WindowId, new CheckYourPupilDataViewModel
        {
            WindowId = WindowId.ToString(),
            SelectedNextStep = NextSteps.ResultsEnquiry,
            WindowEndDate = "", WindowEndTime = "", WindowTitle = "", Sections = [],
            SectionsAsTabs = false, IsWindowOpen = true, OrganisationName = ""
        });

        Assert.Null(_session.GetRequestState(WindowId).SelectedNextStep);
    }

    [Fact]
    public async Task A_rejected_post_reports_a_coded_validation_error()
    {
        Window(CheckingWindowType.KS4June);

        await _sut.NextStep(WindowId, new CheckYourPupilDataViewModel
        {
            WindowId = WindowId.ToString(),
            SelectedNextStep = NextSteps.ResultsEnquiry,
            WindowEndDate = "", WindowEndTime = "", WindowTitle = "", Sections = [],
            SectionsAsTabs = false, IsWindowOpen = true, OrganisationName = ""
        });

        await _analytics.Received(1).TrackSafeAsync(Arg.Is<ValidationErrorEvent>(e =>
            e.ErrorCodes.Contains("no_selection")));
    }

    [Fact]
    public async Task Choosing_it_records_the_choice_in_session()
    {
        Window(CheckingWindowType.Post16);

        await _sut.NextStep(WindowId, new CheckYourPupilDataViewModel
        {
            WindowId = WindowId.ToString(),
            SelectedNextStep = NextSteps.ResultsEnquiry,
            WindowEndDate = "", WindowEndTime = "", WindowTitle = "", Sections = [],
            SectionsAsTabs = false, IsWindowOpen = true, OrganisationName = ""
        });

        Assert.Equal(NextSteps.ResultsEnquiry, _session.GetRequestState(WindowId).SelectedNextStep);
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
        public void Set(string key, byte[] value) => _store[key] = value;
        public void Remove(string key) => _store.Remove(key);
        public void Clear() => _store.Clear();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
    }

    private sealed class TestSessionFeature(ISession session) : ISessionFeature
    {
        public ISession Session { get; set; } = session;
    }
}
