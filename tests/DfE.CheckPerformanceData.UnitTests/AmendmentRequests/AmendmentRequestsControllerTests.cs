using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.AmendmentRequests;
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
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class AmendmentRequestsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IAmendmentRequestsService _service = Substitute.For<IAmendmentRequestsService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IEditAdviceService _adviceService = Substitute.For<IEditAdviceService>();
    private readonly FakeSession _session = new();
    private readonly AmendmentRequestsController _sut;

    public AmendmentRequestsControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new AmendmentRequestsController(_service, _requestService, _adviceService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AmendmentRequestsResult EmptyResult() => new()
    {
        WindowEndDate = new DateTime(2026, 6, 26, 17, 0, 0),
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
