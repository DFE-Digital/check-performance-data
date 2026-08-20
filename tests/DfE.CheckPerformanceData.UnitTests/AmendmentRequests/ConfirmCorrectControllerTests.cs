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

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public sealed class ConfirmCorrectControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string Reference = "CYPMD_KS4June_ABC1234";

    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly IJourneyValidationService _journeyService = Substitute.For<IJourneyValidationService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ConfirmCorrectController _sut;

    public ConfirmCorrectControllerTests()
    {
        _sut = new ConfirmCorrectController(_service, _journeyService, _requestService, _analytics);
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
}
