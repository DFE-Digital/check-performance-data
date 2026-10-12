using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;
// Alias, not a namespace import: WindowManagement also declares a CheckingWindowDto and this
// file already uses the LandingPage one.
using ICheckingExerciseService = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingExerciseService;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using DfE.CheckPerformanceData.Web.Common;
using Microsoft.AspNetCore.Http;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

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
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ConfirmCorrectController _sut;

    public ConfirmCorrectControllerTests()
    {
        var httpContext = new DefaultHttpContext();
        _sut = new ConfirmCorrectController(_service, _journeyService, _requestService, _checkingExercises, _currentUser, _analytics)
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
            ClosedExerciseGuard.MessageFor(CheckingExerciseType.PupilData, LearnerNoun.Pupil),
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

    // AB#307773: pupil data has ended, but an open extension names one school. The gate is asked
    // about the signed-in school, so that school may still confirm and another may not.
    [Theory]
    [InlineData("933/4290", true)]
    [InlineData("860/4070", false)]
    public async Task Index_WhenOnlyTheSchoolsExtensionIsOpen_ShowsThePageToTheSchoolItNamesOnly(string laestab, bool named)
    {
        _currentUser.OrganisationLaestab.Returns(laestab);
        _checkingExercises.OpenOnlyFor("933/4290");

        var result = await _sut.Index(WindowId);

        if (named)
            Assert.IsType<ViewResult>(result);
        else
            Assert.Equal("CheckYourPupilData", Assert.IsType<RedirectToActionResult>(result).ControllerName);
    }

    [Theory]
    [InlineData("933/4290", true)]
    [InlineData("860/4070", false)]
    public async Task Confirm_WhenOnlyTheSchoolsExtensionIsOpen_RecordsTheConfirmationForTheSchoolItNamesOnly(string laestab, bool named)
    {
        _currentUser.OrganisationLaestab.Returns(laestab);
        _checkingExercises.OpenOnlyFor("933/4290");

        await _sut.Confirm(WindowId);

        await _requestService.Received(named ? 1 : 0)
            .ConfirmDataCorrectAsync(WindowId, Reference, Arg.Any<DateTime>(), Arg.Any<EmailSubstitutions>());
    }

    // AB#307773 review C2: the outer window's end moves with every extension, so the page and the
    // email quote the signed-in school's pupil-data end: the extension's for the school it names,
    // the exercise's own for any other.
    [Theory]
    [InlineData("933/4290", 7, "Friday 31 July")]
    [InlineData("860/4070", 6, "Tuesday 30 June")]
    public async Task The_deadline_is_the_signed_in_schools_pupil_data_end(string laestab, int month, string label)
    {
        var extensionEnd = new DateTime(2026, 7, 31, 17, 0, 0);
        var exerciseEnd = new DateTime(2026, 6, 30, 17, 0, 0);
        _currentUser.OrganisationLaestab.Returns(laestab);
        _service.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "KS4 June",
            StartDate = new DateTime(2026, 6, 1),
            EndDate = extensionEnd,   // decision 8: the outer end includes the extension
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            Exercises =
            [
                new Application.WindowManagement.CheckingExerciseDto
                {
                    Id = Guid.NewGuid(),
                    ExerciseType = CheckingExerciseType.PupilData,
                    StartDate = new DateTime(2026, 6, 1),
                    EndDate = exerciseEnd,
                    Extensions =
                    [
                        new Application.WindowManagement.CheckingExerciseExtensionDto
                        {
                            Id = Guid.NewGuid(), EndDate = extensionEnd, Laestabs = ["9334290"]
                        }
                    ]
                }
            ]
        });
        var expected = month == 7 ? extensionEnd : exerciseEnd;

        var page = Assert.IsType<ConfirmCorrectViewModel>(Assert.IsType<ViewResult>(await _sut.Index(WindowId)).Model);
        var confirmed = Assert.IsType<ConfirmedCorrectViewModel>(Assert.IsType<ViewResult>(await _sut.Confirm(WindowId)).Model);

        Assert.EndsWith(label, page.EndDate);
        Assert.EndsWith(label, confirmed.EndDate);
        await _requestService.Received(1)
            .ConfirmDataCorrectAsync(WindowId, Reference, expected, Arg.Any<EmailSubstitutions>());
    }
}
