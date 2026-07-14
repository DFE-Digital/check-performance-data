using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class AmendmentRequestsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IAmendmentRequestsService _service = Substitute.For<IAmendmentRequestsService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IEditAdviceService _adviceService = Substitute.For<IEditAdviceService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly IBulkSubmissionService _bulkService = Substitute.For<IBulkSubmissionService>();
    private readonly ICheckYourPupilDataService _checkYourPupilData = Substitute.For<ICheckYourPupilDataService>();
    private readonly FakeSession _session = new();
    private readonly AmendmentRequestsController _sut;

    public AmendmentRequestsControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new AmendmentRequestsController(_service, _requestService, _adviceService, _analytics, _bulkService, _checkYourPupilData)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        _sut.TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());
    }

    // ── Index ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_ReturnsViewResult()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(EmptyResult());

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Index_CallsServiceWithWindowId()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(EmptyResult());

        await _sut.Index(WindowId);

        await _service.Received(1).GetAmendmentRequestsAsync(WindowId);
    }

    [Fact]
    public async Task Index_FormatsDeadlineText()
    {
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            WindowEndDate = endDate,
            WindowTitle = "Key stage 4",
            Rows = [],
            SubmittedRows = []
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Equal("5pm on Friday 26 June 2026", vm.DeadlineText);
    }

    [Fact]
    public async Task Index_MapsRowsToViewModel()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            WindowEndDate = DateTime.UtcNow,
            WindowTitle = "Key stage 4",
            Rows =
            [
                new AmendmentRequestDto
                {
                    PupilName = "Jane Smith",
                    RequestType = RequestType.Amendment,
                    RequestTypeDescription = "Remove - Permanently left England",
                    Status = RequestStatus.ReadyToSubmit,
                    ReferenceNumber = "REF001"
                }
            ],
            SubmittedRows = []
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Single(vm.Rows);
        Assert.Equal("Jane Smith", vm.Rows[0].PupilName);
        Assert.Equal(RequestType.Amendment, vm.Rows[0].RequestType);
        Assert.Equal("Remove - Permanently left England", vm.Rows[0].RequestTypeDescription);
        Assert.Equal(RequestStatus.ReadyToSubmit, vm.Rows[0].Status);
        Assert.Equal("REF001", vm.Rows[0].ReferenceNumber);
    }

    [Fact]
    public async Task Index_MapsSubmittedRowsToViewModel()
    {
        var submitted = new DateTime(2026, 6, 16, 9, 30, 0);
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            WindowEndDate = DateTime.UtcNow,
            WindowTitle = "Key stage 4",
            Rows = [],
            SubmittedRows =
            [
                new SubmittedRequestDto
                {
                    PupilName = "John Doe",
                    RequestType = RequestType.Amendment,
                    RequestTypeDescription = "Remove - Permanently left England",
                    ReferenceNumber = "REF010",
                    Status = RequestStatus.SubmittedUnCommitted,
                    Submitted = submitted
                }
            ]
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Single(vm.SubmittedRows);
        Assert.Equal("John Doe", vm.SubmittedRows[0].PupilName);
        Assert.Equal("REF010", vm.SubmittedRows[0].ReferenceNumber);
        Assert.Equal(submitted, vm.SubmittedRows[0].Submitted);
        Assert.Equal("govuk-tag--green", vm.SubmittedRows[0].TagClass);
        Assert.Equal("Submitted", vm.SubmittedRows[0].TagLabel);
        Assert.Equal("16 June 2026", vm.SubmittedRows[0].SubmittedDateText);
    }

    [Fact]
    public async Task Index_WhenSubmittedRowWithdrawn_UsesGreyWithdrawnTag()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            WindowEndDate = DateTime.UtcNow,
            WindowTitle = "Key stage 4",
            Rows = [],
            SubmittedRows =
            [
                new SubmittedRequestDto
                {
                    PupilName = "John Doe",
                    RequestType = RequestType.Amendment,
                    RequestTypeDescription = "Remove - Permanently left England",
                    ReferenceNumber = "REF011",
                    Status = RequestStatus.Withdrawn,
                    Submitted = DateTime.UtcNow
                }
            ]
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Equal("govuk-tag--grey", vm.SubmittedRows[0].TagClass);
        Assert.Equal("Withdrawn", vm.SubmittedRows[0].TagLabel);
    }

    [Fact]
    public async Task Index_SetsWindowId()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(EmptyResult());

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Equal(WindowId, vm.WindowId);
    }

    // ── Edit (advice screen) ───────────────────────────────────────────────────

    [Fact]
    public async Task Edit_WhenDraftNotFound_RedirectsToIndex()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns((RequestState?)null);

        var result = await _sut.Edit(WindowId, "REF001");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("AmendmentRequests", redirect.ControllerName);
    }

    [Fact]
    public async Task Edit_WhenAdviceNull_RedirectsToIndex()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns((AmendmentAdvice?)null);

        var result = await _sut.Edit(WindowId, "REF001");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task Edit_WhenDraftFound_WritesJourneyToSession()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns(SampleAdvice());

        await _sut.Edit(WindowId, "REF001");

        var restored = _session.GetRequestState(WindowId);
        Assert.Equal("REF001", restored.ReferenceNumber);
        Assert.Equal(WhatToChange.Remove, restored.SelectedWhatToChange);
    }

    [Fact]
    public async Task Edit_WhenAdviceBuilt_ReturnsAdviceView()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns(SampleAdvice());

        var result = await _sut.Edit(WindowId, "REF001");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("EditAdvice", view.ViewName);
        var vm = Assert.IsType<EditAdviceViewModel>(view.Model);
        Assert.Equal("REF001", vm.ReferenceNumber);
        Assert.Equal("This request is to remove a pupil. If this is not correct, go back and delete the request.", vm.AdviceText);
    }

    [Fact]
    public async Task Edit_Default_SetsBackUrlToIndex()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns(SampleAdvice());

        var result = await _sut.Edit(WindowId, "REF001");

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<EditAdviceViewModel>(view.Model);
        Assert.Equal($"/{WindowId}/AmendmentRequests", vm.BackUrl);
    }

    [Fact]
    public async Task Edit_FromBulk_SetsBackUrlToBulkReviewPage()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns(SampleAdvice());

        var result = await _sut.Edit(WindowId, "REF001", fromBulk: true);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<EditAdviceViewModel>(view.Model);
        Assert.Equal($"/{WindowId}/AmendmentRequests/bulk", vm.BackUrl);
    }

    // ── Continue ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Continue_WhenTargetIsPage_RedirectsToJourneyPage()
    {
        _session.SetRequestState(WindowId, SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>())
            .Returns(SampleAdvice(new ContinueToPage("reason")));

        var result = await _sut.Continue(WindowId, "REF001");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Page", redirect.ActionName);
        Assert.Equal("Journey", redirect.ControllerName);
        Assert.Equal("reason", redirect.RouteValues!["pageId"]);
    }

    [Fact]
    public async Task Continue_WhenTargetIsSummary_RedirectsToJourneySummary()
    {
        _session.SetRequestState(WindowId, SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>())
            .Returns(SampleAdvice(new ContinueToSummary()));

        var result = await _sut.Continue(WindowId, "REF001");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ActionName);
        Assert.Equal("Journey", redirect.ControllerName);
    }

    [Fact]
    public async Task Continue_WhenSessionNotReady_RedirectsToIndex()
    {
        var result = await _sut.Continue(WindowId, "REF001");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task Edit_WhenDraftFound_EmitsDraftResumedEvent()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());

        await _sut.Edit(WindowId, "REF001");

        await _analytics.Received(1).TrackAsync(
            Arg.Is<DraftResumedEvent>(e =>
                e.ReferenceNumber == "REF001" &&
                e.WhatToChange == "Remove" &&
                e.CheckingWindowType == "KS4June"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_WhenDraftNotFound_DoesNotEmit()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns((RequestState?)null);

        await _sut.Edit(WindowId, "REF001");

        await _analytics.DidNotReceive().TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>());
    }

    // ── Bulk submission ────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkReview_NoSelection_RedisplaysIndexWithError()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(EmptyResult());

        var result = await _sut.BulkReview(WindowId, Array.Empty<string>());

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        Assert.False(_sut.ModelState.IsValid);
    }

    [Fact]
    public async Task BulkReview_WithSelection_StoresSelectionAndRedirectsToBulkReviewPage()
    {
        var result = await _sut.BulkReview(WindowId, new[] { "R1", "R2" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AmendmentRequestsController.BulkReviewPage), redirect.ActionName);
        Assert.Equal(new[] { "R1", "R2" }, _session.GetBulkSelection(WindowId));
    }

    [Fact]
    public async Task BulkReviewPage_WithStoredSelection_RendersBulkReview()
    {
        _session.SetBulkSelection(WindowId, new[] { "R1" });
        _bulkService.BuildReviewAsync(WindowId, Arg.Any<IReadOnlyList<string>>()).Returns(new BulkReviewResult
        {
            Submittable = new[] { new BulkReviewItem { ReferenceNumber = "R1", PupilName = "Ann Alpha", RequestTypeDescription = "Remove pupil" } },
            Duplicates = Array.Empty<BulkReviewItem>()
        });
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _checkYourPupilData.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 2026",
            EndDate = endDate,
            StartDate = endDate.AddMonths(-3),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June
        });

        var result = await _sut.BulkReviewPage(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<BulkReviewViewModel>(view.Model);
        Assert.Single(vm.Submittable);
    }

    [Fact]
    public async Task BulkReviewPage_NoStoredSelection_RedirectsToIndex()
    {
        var result = await _sut.BulkReviewPage(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AmendmentRequestsController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task BulkSubmit_SubmitsAndRedirectsToConfirmation()
    {
        _bulkService.SubmitAsync(WindowId, Arg.Any<IReadOnlyList<string>>()).Returns(new BulkSubmissionResult
        {
            Submitted = new[] { "R1", "R2" },
            Skipped = Array.Empty<string>()
        });

        var result = await _sut.BulkSubmit(WindowId, new[] { "R1", "R2" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AmendmentRequestsController.BulkConfirmation), redirect.ActionName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
        Assert.Equal("R1,R2", _sut.TempData["BulkSubmittedRefs"]);
    }

    [Fact]
    public async Task BulkSubmit_ClearsStoredBulkSelection()
    {
        _session.SetBulkSelection(WindowId, new[] { "R1", "R2" });
        _bulkService.SubmitAsync(WindowId, Arg.Any<IReadOnlyList<string>>()).Returns(new BulkSubmissionResult
        {
            Submitted = new[] { "R1", "R2" },
            Skipped = Array.Empty<string>()
        });

        await _sut.BulkSubmit(WindowId, new[] { "R1", "R2" });

        Assert.Empty(_session.GetBulkSelection(WindowId));
    }

    [Fact]
    public async Task BulkConfirmation_NoTempDataRefs_RedirectsToIndex()
    {
        var result = await _sut.BulkConfirmation(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AmendmentRequestsController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task BulkConfirmation_WithRefs_RendersConfirmationWithLowercaseDeadline()
    {
        _sut.TempData["BulkSubmittedRefs"] = "R1,R2";
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _checkYourPupilData.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 2026",
            EndDate = endDate,
            StartDate = endDate.AddMonths(-3),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June
        });

        var result = await _sut.BulkConfirmation(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<BulkConfirmationViewModel>(view.Model);
        Assert.Equal(new[] { "R1", "R2" }, vm.ReferenceNumbers);
        Assert.Contains("5pm", vm.WindowCloseLabel); // lowercase am/pm
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AmendmentRequestsResult EmptyResult() => new()
    {
        WindowEndDate = new DateTime(2026, 6, 26, 17, 0, 0),
        WindowTitle = "Key stage 4",
        Rows = [],
        SubmittedRows = []
    };

    private static RequestState SampleJourney() => new()
    {
        ReferenceNumber = "REF001",
        SelectedWhatToChange = WhatToChange.Remove,
        CheckingWindow = new CheckingWindowDto
        {
            Id = Guid.NewGuid(),
            Title = "Test Window",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(10)
        }
    };

    private static AmendmentAdvice SampleAdvice(AmendmentContinueTarget? target = null) => new()
    {
        RequestType = WhatToChange.Remove,
        PupilName = "Jane Smith",
        AdviceText = "This request is to remove a pupil. If this is not correct, go back and delete the request.",
        EvidenceMessages = [],
        ContinueTarget = target ?? new ContinueToSummary()
    };

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool TryGetValue(string key, out byte[] value) =>
            _store.TryGetValue(key, out value!);

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
