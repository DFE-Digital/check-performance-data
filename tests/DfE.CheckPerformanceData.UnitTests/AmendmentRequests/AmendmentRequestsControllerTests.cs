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
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Alias, not a namespace import: WindowManagement also declares a CheckingWindowDto and this
// file already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using DfE.CheckPerformanceData.Web.Common;

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
    private readonly IQuestionFlowService _flowService = Substitute.For<IQuestionFlowService>();
    private readonly IJourneyViewModelBuilder _viewModelBuilder = Substitute.For<IJourneyViewModelBuilder>();
    private readonly FakeSession _session = new();
    private readonly ICheckingExerciseService _checkingExercises = OpenCheckingExercises.AlwaysOpen();
    private readonly AmendmentRequestsController _sut;

    public AmendmentRequestsControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new AmendmentRequestsController(_service, _requestService, _adviceService, _analytics, _bulkService, _checkYourPupilData, _flowService, _checkingExercises, _viewModelBuilder)
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
    public async Task Index_MarksRowsSelectedFromStoredBulkSelection()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            Deadlines = [Deadline(new DateTime(2026, 6, 26, 17, 0, 0))],
            WindowTitle = "Key stage 4",
            Rows =
            [
                new AmendmentRequestDto { PupilName = "Ann Alpha", RequestType = RequestType.Amendment, RequestTypeDescription = "Remove pupil", Status = RequestStatus.ReadyToSubmit, ReferenceNumber = "R1" },
                new AmendmentRequestDto { PupilName = "Bob Beta", RequestType = RequestType.Amendment, RequestTypeDescription = "Remove pupil", Status = RequestStatus.ReadyToSubmit, ReferenceNumber = "R2" }
            ],
            SubmittedRows = []
        });
        _session.SetBulkSelection(WindowId, new[] { "R1" });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.True(vm.Rows.Single(r => r.ReferenceNumber == "R1").IsSelected);
        Assert.False(vm.Rows.Single(r => r.ReferenceNumber == "R2").IsSelected);
    }

    [Fact]
    public async Task Index_FormatsDeadlineText()
    {
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            Deadlines = [Deadline(endDate)],
            WindowTitle = "Key stage 4",
            Rows = [],
            SubmittedRows = []
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Equal("5pm on Friday 26 June 2026", Assert.Single(vm.Deadlines).DeadlineText);
    }

    // #320: one deadline line per checking exercise, each on its own dates. Before this the page
    // printed the outer window's end once, which on a 16-19 window is the results-enquiry close —
    // months after pupil data shuts, so it told a school it still had time it did not have.
    [Fact]
    public async Task Index_GivesEachCheckingExercise_ItsOwnDeadlineLine()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            Deadlines =
            [
                Deadline(new DateTime(2026, 10, 18, 17, 0, 0)),
                Deadline(new DateTime(2027, 3, 31, 17, 0, 0),
                    CheckingExerciseType.ResultsEnquiry, isOpen: false)
            ],
            WindowTitle = "16 to 19",
            Rows = [],
            SubmittedRows = []
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Equal(
            [CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry],
            vm.Deadlines.Select(d => d.Exercise));
        Assert.Equal("5pm on Sunday 18 October 2026", vm.Deadlines[0].DeadlineText);
        Assert.Equal("5pm on Wednesday 31 March 2027", vm.Deadlines[1].DeadlineText);
    }

    [Fact]
    public async Task Index_AnOpenExercise_SaysSubmitBy_AndAClosedOne_SaysThePassed()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            Deadlines =
            [
                Deadline(new DateTime(2026, 10, 18, 17, 0, 0)),
                Deadline(new DateTime(2026, 6, 26, 17, 0, 0),
                    CheckingExerciseType.ResultsEnquiry, isOpen: false)
            ],
            WindowTitle = "16 to 19",
            Rows = [],
            SubmittedRows = []
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.StartsWith("Submit your", vm.Deadlines[0].Sentence);
        Assert.StartsWith("The deadline for", vm.Deadlines[1].Sentence);
    }

    [Fact]
    public async Task Index_MapsRowsToViewModel()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(new AmendmentRequestsResult
        {
            Deadlines = [Deadline(DateTime.UtcNow)],
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
            Deadlines = [Deadline(DateTime.UtcNow)],
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
            Deadlines = [Deadline(DateTime.UtcNow)],
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
    public async Task Edit_FromBulk_RedirectsToJourneySummaryBypassingAdvice()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());

        var result = await _sut.Edit(WindowId, "REF001", fromBulk: true);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Summary", redirect.ActionName);
        Assert.Equal("Journey", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
        // The edit-advice interstitial is skipped entirely on the bulk path.
        await _adviceService.DidNotReceive().BuildAsync(WindowId, "REF001", Arg.Any<RequestState>());
        // The summary uses this flag to link back to the batch and hide its submit/save actions.
        Assert.True(_session.IsBulkEditMode(WindowId));
    }

    [Fact]
    public async Task Edit_NotFromBulk_ClearsBulkEditMode()
    {
        _session.SetBulkEditMode(WindowId);
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns(SampleAdvice());

        await _sut.Edit(WindowId, "REF001");

        Assert.False(_session.IsBulkEditMode(WindowId));
    }

    [Fact]
    public async Task Edit_NotFromBulk_SetsSingleEditMode()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns(SampleAdvice());

        await _sut.Edit(WindowId, "REF001");

        // The summary uses this flag to link back to the Amendment Requests page.
        Assert.True(_session.IsSingleEditMode(WindowId));
    }

    [Fact]
    public async Task Edit_FromBulk_ClearsSingleEditMode()
    {
        _session.SetSingleEditMode(WindowId);
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());

        await _sut.Edit(WindowId, "REF001", fromBulk: true);

        Assert.False(_session.IsSingleEditMode(WindowId));
    }

    [Fact]
    public async Task Edit_FromBulk_PrimesSessionForSummary()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());

        await _sut.Edit(WindowId, "REF001", fromBulk: true);

        var restored = _session.GetRequestState(WindowId);
        Assert.Equal("REF001", restored.ReferenceNumber);
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

    // ── BulkReviewDetailed ────────────────────────────────────────────────────

    [Fact]
    public async Task BulkReviewDetailed_NoSelection_ReturnsIndexWithError()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(EmptyResult());

        var result = await _sut.BulkReviewDetailed(WindowId, Array.Empty<string>());

        Assert.IsType<ViewResult>(result);
        Assert.False(_sut.ModelState.IsValid);
    }

    [Fact]
    public async Task BulkReviewDetailed_WithSelection_StoresSelectionAndRedirectsToDetailedPage()
    {
        var result = await _sut.BulkReviewDetailed(WindowId, new[] { "R1", "R2" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AmendmentRequestsController.BulkReviewDetailedPage), redirect.ActionName);
        Assert.Equal(new[] { "R1", "R2" }, _session.GetBulkSelection(WindowId));
    }

    [Fact]
    public async Task BulkReviewDetailedPage_NoStoredSelection_RedirectsToIndex()
    {
        var result = await _sut.BulkReviewDetailedPage(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AmendmentRequestsController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task BulkReviewDetailedPage_BuildsSummaryPerSubmittableAndKeepsDuplicates()
    {
        _session.SetBulkSelection(WindowId, new[] { "R1", "DUP1" });
        _bulkService.BuildReviewAsync(WindowId, Arg.Any<IReadOnlyList<string>>()).Returns(new BulkReviewResult
        {
            Submittable = new[] { new BulkReviewItem { ReferenceNumber = "R1", PupilName = "Ann Alpha", RequestTypeDescription = "Remove pupil" } },
            Duplicates = new[] { new BulkReviewItem { ReferenceNumber = "DUP1", PupilName = "Bob Beta", RequestTypeDescription = "Remove pupil", DuplicateReason = "Already submitted" } }
        });
        _checkYourPupilData.GetCheckingWindowAsync(WindowId).Returns(SampleWindow());

        var journey = SampleJourney();
        journey.QuestionHistory.Add("reason");
        _requestService.ResumeDraftAsync(WindowId, "R1").Returns(journey);
        var config = new QuestionFlowConfig { FirstPageId = "reason", Pages = [] };
        _flowService.GetConfigAsync(WhatToChange.Remove, CheckingWindowType.KS4June).Returns(config);
        _viewModelBuilder.BuildSummaryVm(WindowId, journey, config).Returns(SampleSummaryVm());

        var result = await _sut.BulkReviewDetailedPage(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<BulkReviewDetailedViewModel>(view.Model);
        var item = Assert.Single(vm.Submittable);
        Assert.Equal("R1", item.ReferenceNumber);
        Assert.Single(vm.Duplicates);
    }

    [Fact]
    public async Task BulkReviewDetailedPage_SkipsSubmittableWhoseDraftCannotBeResumed()
    {
        _session.SetBulkSelection(WindowId, new[] { "R1" });
        _bulkService.BuildReviewAsync(WindowId, Arg.Any<IReadOnlyList<string>>()).Returns(new BulkReviewResult
        {
            Submittable = new[] { new BulkReviewItem { ReferenceNumber = "R1", PupilName = "Ann Alpha", RequestTypeDescription = "Remove pupil" } },
            Duplicates = Array.Empty<BulkReviewItem>()
        });
        _checkYourPupilData.GetCheckingWindowAsync(WindowId).Returns(SampleWindow());
        _requestService.ResumeDraftAsync(WindowId, "R1").Returns((RequestState?)null);

        var result = await _sut.BulkReviewDetailedPage(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<BulkReviewDetailedViewModel>(view.Model);
        Assert.Empty(vm.Submittable);
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
        _checkYourPupilData.GetCheckingWindowAsync(WindowId).Returns(SampleWindow());

        var result = await _sut.BulkConfirmation(WindowId);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<BulkConfirmationViewModel>(view.Model);
        Assert.Equal(new[] { "R1", "R2" }, vm.ReferenceNumbers);
        Assert.Contains("5pm", vm.WindowCloseLabel); // lowercase am/pm
    }

    // #320: the banner offers another amendment, so it must quote the pupil-data close. A window
    // that runs no pupil data checking has no such date, and the banner is dropped rather than
    // falling back to the outer window's end — which on a 16-19 window is months later.
    [Fact]
    public async Task BulkConfirmation_WhenTheWindowHasNoPupilDataExercise_HasNoDeadlineLabel()
    {
        _sut.TempData["BulkSubmittedRefs"] = "R1";
        _checkYourPupilData.GetCheckingWindowAsync(WindowId).Returns(SampleWindow(withExercises: false));

        var result = await _sut.BulkConfirmation(WindowId);

        var vm = Assert.IsType<BulkConfirmationViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Null(vm.WindowCloseLabel);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ExerciseDeadlineDto Deadline(
        DateTime endDate,
        CheckingExerciseType exercise = CheckingExerciseType.PupilData,
        bool isOpen = true) =>
        new() { Exercise = exercise, EndDate = endDate, IsOpen = isOpen };

    private static AmendmentRequestsResult EmptyResult() => new()
    {
        Deadlines = [Deadline(new DateTime(2026, 6, 26, 17, 0, 0))],
        WindowTitle = "Key stage 4",
        Rows = [],
        SubmittedRows = []
    };

    private static CheckingWindowDto SampleWindow(bool withExercises = true)
    {
        var endDate = new DateTime(2026, 6, 26, 17, 0, 0);
        return new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 2026",
            EndDate = endDate,
            StartDate = endDate.AddMonths(-3),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            // The bulk confirmation banner reads the pupil-data exercise's end, not the window's.
            Exercises = withExercises
                ?
                [
                    new CheckingExerciseDto
                    {
                        ExerciseType = CheckingExerciseType.PupilData,
                        StartDate = endDate.AddMonths(-3),
                        EndDate = endDate,
                        SortOrder = 0
                    }
                ]
                : []
        };
    }

    private static SummaryViewModel SampleSummaryVm() => new()
    {
        WindowId = WindowId,
        WhatToChange = WhatToChange.Remove,
        PupilName = "Ann Alpha",
        Rows = [],
        FileRows = [],
        BackPageId = "reason",
        MaxEvidencePages = 6
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

    // ── #318: a draft whose checking exercise has closed ─────────────────────

    [Fact]
    public async Task Edit_WhenTheDraftsExerciseHasClosed_RedirectsToCheckYourPupilDataWithAMessage()
    {
        // Product decision 2026-08-20: block the resume rather than the submit, so a user never
        // edits a request that could not be sent.
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _adviceService.BuildAsync(WindowId, "REF001", Arg.Any<RequestState>()).Returns(SampleAdvice());
        _checkingExercises.Close();

        var result = await _sut.Edit(WindowId, "REF001");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal(
            ClosedExerciseGuard.MessageFor(CheckingExerciseType.PupilData),
            _sut.TempData[ClosedExerciseGuard.TempDataKey]);
    }

    [Fact]
    public async Task Edit_WhenTheDraftsExerciseHasClosed_DoesNotPrimeTheJourneySession()
    {
        _requestService.ResumeDraftAsync(WindowId, "REF001").Returns(SampleJourney());
        _checkingExercises.Close();

        await _sut.Edit(WindowId, "REF001");

        Assert.Null(_session.GetRequestState(WindowId).SelectedWhatToChange);
    }

}
