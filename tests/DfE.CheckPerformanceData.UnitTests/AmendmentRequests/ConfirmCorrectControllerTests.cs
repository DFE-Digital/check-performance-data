using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Alias, not a namespace import: WindowManagement also declares a CheckingWindowDto and this
// file already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using DfE.CheckPerformanceData.Web.Common;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public sealed class ConfirmCorrectControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string Reference = "CYPMD_KS4June_ABC1234";

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ICheckingExerciseService _checkingExercises = OpenCheckingExercises.AlwaysOpen();
    private readonly ConfirmCorrectController _sut;

    public ConfirmCorrectControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        _sut = new ConfirmCorrectController(_service, _journeyService, _requestService, _checkingExercises, _analytics)
        {
            // #318: the closed-exercise gate stashes its message in TempData before redirecting.
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>())
        };
        _service.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            StartDate = new DateTime(2026, 6, 1),
            EndDate = new DateTime(2026, 6, 30, 17, 0, 0),
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
        });
        _journeyService.GenerateReference(CheckingWindowType.KS4June).Returns(Reference);
    }

    [Fact]
    public async Task Confirm_PersistsConfirmation_AndEmitsCorrectDataConfirmedEvent()
    {
        var result = await _sut.Confirm(WindowId);

        Assert.IsType<ViewResult>(result);
        await _requestService.Received(1).ConfirmDataCorrectAsync(WindowId, Reference, Arg.Any<DateTime>(), Arg.Any<EmailSubstitutions>());
        await _analytics.Received(1).TrackAsync(
            Arg.Is<CorrectDataConfirmedEvent>(e =>
                e.ReferenceNumber == Reference &&
                e.CheckingWindowType == "KS4June"),
            Arg.Any<CancellationToken>());

        // The event must fire only after the confirmation is persisted.
        Received.InOrder(() =>
        {
            _ = _requestService.ConfirmDataCorrectAsync(WindowId, Reference, Arg.Any<DateTime>(), Arg.Any<EmailSubstitutions>());
            _ = _analytics.TrackAsync(Arg.Any<CorrectDataConfirmedEvent>(), Arg.Any<CancellationToken>());
        });
    }

    // ── #318: closed pupil-data checking exercise ────────────────────────────

    [Fact]
    public async Task Index_WhenPupilDataExerciseClosed_RedirectsToCheckYourPupilDataWithAMessage()
    {
        _checkingExercises.Close();

        var result = await _sut.Index(WindowId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("CheckYourPupilData", redirect.ControllerName);
        Assert.Equal(
            ClosedExerciseGuard.MessageFor(CheckingExerciseType.PupilData),
            _sut.TempData[ClosedExerciseGuard.TempDataKey]);
    }

    [Fact]
    public async Task Confirm_WhenPupilDataExerciseClosed_RecordsNothing()
    {
        _checkingExercises.Close();

        var result = await _sut.Confirm(WindowId);

        Assert.IsType<RedirectToActionResult>(result);
        await _requestService.DidNotReceiveWithAnyArgs()
            .ConfirmDataCorrectAsync(default, default!, default, default!);
        await _analytics.DidNotReceive()
            .TrackAsync(Arg.Any<CorrectDataConfirmedEvent>(), Arg.Any<CancellationToken>());
    }

}
