using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class SubmittedRequestControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const string Reference = "CYPMD_KS4June_ABC1234";

    private readonly ISubmittedRequestService _service = Substitute.For<ISubmittedRequestService>();
    private readonly IRequestService _requestService = Substitute.For<IRequestService>();
    private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly SubmittedRequestController _sut;

    public SubmittedRequestControllerTests()
    {
        _sut = new SubmittedRequestController(_service, _requestService, _fileStorage, _analytics)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task View_WhenNotFound_RedirectsToAmendmentRequests()
    {
        _service.GetAsync(WindowId, Reference).Returns((SubmittedRequestView?)null);

        var result = await _sut.View(WindowId, Reference);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("AmendmentRequests", redirect.ControllerName);
    }

    [Fact]
    public async Task View_WhenFound_ReturnsViewModel()
    {
        var submittedAt = new DateTime(2026, 6, 16, 9, 30, 0);
        _service.GetAsync(WindowId, Reference).Returns(new SubmittedRequestView
        {
            WhatToChange = WhatToChange.Remove,
            Status = RequestStatus.SubmittedUnCommitted,
            PupilName = "Jane Smith",
            Rows = [new SubmittedRequestAnswerRow { Title = "Why?", DisplayValue = "Left England" }],
            Files = [],
            ReferenceNumber = Reference,
            SubmittedByEmail = "submitter@education.gov.uk",
            SubmittedAt = submittedAt
        });

        var result = await _sut.View(WindowId, Reference);

        var vm = Assert.IsType<SubmittedRequestViewModel>(((ViewResult)result).Model);
        Assert.Equal(WindowId, vm.WindowId);
        Assert.Equal("Jane Smith", vm.PupilName);
        Assert.Single(vm.Rows);
        Assert.Equal("submitter@education.gov.uk", vm.SubmittedByEmail);
        Assert.Equal(submittedAt, vm.SubmittedAt);
        Assert.Equal(Reference, vm.ReferenceNumber);
    }

    [Fact]
    public async Task ViewConfirmation_WhenNotFound_RedirectsToAmendmentRequests()
    {
        _service.GetConfirmDataCorrectAsync(WindowId, Reference).Returns((ConfirmDataCorrectView?)null);

        var result = await _sut.ViewConfirmation(WindowId, Reference);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("AmendmentRequests", redirect.ControllerName);
    }

    [Fact]
    public async Task ViewConfirmation_WhenFound_ReturnsViewModel()
    {
        var submittedAt = new DateTime(2026, 6, 16, 9, 30, 0);
        _service.GetConfirmDataCorrectAsync(WindowId, Reference).Returns(new ConfirmDataCorrectView
        {
            Status = RequestStatus.SubmittedUnCommitted,
            SubmittedByEmail = "submitter@education.gov.uk",
            SubmittedAt = submittedAt,
            ReferenceNumber = Reference
        });

        var result = await _sut.ViewConfirmation(WindowId, Reference);

        var vm = Assert.IsType<ConfirmDataCorrectViewModel>(((ViewResult)result).Model);
        Assert.Equal(WindowId, vm.WindowId);
        Assert.Equal("submitter@education.gov.uk", vm.SubmittedByEmail);
        Assert.Equal(submittedAt, vm.SubmittedAt);
        Assert.Equal(Reference, vm.ReferenceNumber);
        Assert.Equal("Confirm pupil data is correct", vm.RequestTypeDisplay);
    }

    [Fact]
    public async Task DownloadEvidence_WhenStoredNameNotGuid_ReturnsNotFound()
    {
        var result = await _sut.DownloadEvidence(WindowId, Reference, "not-a-guid");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DownloadEvidence_WhenFileNotInRequest_ReturnsNotFound()
    {
        var fileId = "55555555-5555-5555-5555-555555555555";
        _service.GetAsync(WindowId, Reference).Returns(new SubmittedRequestView
        {
            WhatToChange = WhatToChange.Remove,
            Status = RequestStatus.SubmittedUnCommitted,
            PupilName = "Jane Smith",
            Rows = [],
            Files = [],
            ReferenceNumber = Reference
        });

        var result = await _sut.DownloadEvidence(WindowId, Reference, fileId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DownloadEvidence_WhenFilePresent_ReturnsPdf()
    {
        var fileId = "55555555-5555-5555-5555-555555555555";
        _service.GetAsync(WindowId, Reference).Returns(new SubmittedRequestView
        {
            WhatToChange = WhatToChange.Remove,
            Status = RequestStatus.SubmittedUnCommitted,
            PupilName = "Jane Smith",
            Rows = [],
            Files =
            [
                new SubmittedRequestFileRow
                {
                    OriginalFileName = "evidence.pdf",
                    StoredFileName = fileId,
                    FileSizeBytes = 1024,
                    PageCount = 1
                }
            ],
            ReferenceNumber = Reference
        });
        _fileStorage.GetAsync(WindowId, fileId).Returns([1, 2, 3]);

        var result = await _sut.DownloadEvidence(WindowId, Reference, fileId);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("evidence.pdf", file.FileDownloadName);
    }

    [Fact]
    public async Task ConfirmDelete_WhenFound_ReturnsViewInConfirmMode()
    {
        _service.GetAsync(WindowId, Reference).Returns(new SubmittedRequestView
        {
            WhatToChange = WhatToChange.Remove,
            Status = RequestStatus.SubmittedUnCommitted,
            PupilName = "Jane Smith",
            Rows = [],
            Files = [],
            ReferenceNumber = Reference
        });

        var result = await _sut.ConfirmDelete(WindowId, Reference);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("View", view.ViewName);
        var vm = Assert.IsType<SubmittedRequestViewModel>(view.Model);
        Assert.True(vm.ConfirmingDelete);
    }

    [Fact]
    public async Task ConfirmDeleteConfirmation_WhenFound_ReturnsViewInConfirmMode()
    {
        _service.GetConfirmDataCorrectAsync(WindowId, Reference).Returns(new ConfirmDataCorrectView
        {
            Status = RequestStatus.SubmittedUnCommitted,
            SubmittedByEmail = "submitter@education.gov.uk",
            SubmittedAt = new DateTime(2026, 6, 16, 9, 30, 0),
            ReferenceNumber = Reference
        });

        var result = await _sut.ConfirmDeleteConfirmation(WindowId, Reference);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ViewConfirmation", view.ViewName);
        var vm = Assert.IsType<ConfirmDataCorrectViewModel>(view.Model);
        Assert.True(vm.ConfirmingDelete);
    }

    [Fact]
    public async Task Delete_DeletesAndRedirectsToAmendmentRequests()
    {
        _requestService.DeleteAsync(WindowId, Reference).Returns(new RequestDeletionResult(false, "Jane Smith", RequestType.Amendment));

        var result = await _sut.Delete(WindowId, Reference);

        await _requestService.Received(1).DeleteAsync(WindowId, Reference);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("AmendmentRequests", redirect.ControllerName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
    }

    [Fact]
    public async Task Delete_WhenHardDeleted_SetsConfirmationMessage()
    {
        _requestService.DeleteAsync(WindowId, Reference).Returns(new RequestDeletionResult(true, "Jane Smith", RequestType.Amendment));

        await _sut.Delete(WindowId, Reference);

        Assert.Equal("Jane Smith has been removed from your saved request", _sut.TempData["DeletedMessage"]);
    }

    [Fact]
    public async Task Delete_WhenWithdrawn_SetsSubmittedConfirmationMessage()
    {
        _requestService.DeleteAsync(WindowId, Reference).Returns(new RequestDeletionResult(false, "Jane Smith", RequestType.Amendment));

        await _sut.Delete(WindowId, Reference);

        Assert.Equal($"Jane Smith(reference number - {Reference}) has been removed from your submitted request.", _sut.TempData["DeletedMessage"]);
    }

    [Fact]
    public async Task Delete_Amendment_EmitsAmendmentRequestDeletedEvent()
    {
        _requestService.DeleteAsync(WindowId, Reference).Returns(new RequestDeletionResult(true, "Jane Smith", RequestType.Amendment));

        await _sut.Delete(WindowId, Reference);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<AmendmentRequestDeletedEvent>(e =>
                e.ReferenceNumber == Reference &&
                e.WasHardDeleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_Confirmation_EmitsConfirmationDeletedEvent()
    {
        _requestService.DeleteAsync(WindowId, Reference).Returns(new RequestDeletionResult(false, "Jane Smith", RequestType.ConfirmCorrect));

        await _sut.Delete(WindowId, Reference);

        await _analytics.Received(1).TrackAsync(
            Arg.Is<ConfirmationDeletedEvent>(e => e.ReferenceNumber == Reference),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WhenNoRowWasFound_EmitsNoEvent()
    {
        _requestService.DeleteAsync(WindowId, Reference).Returns(new RequestDeletionResult(false, "", null));

        await _sut.Delete(WindowId, Reference);

        await _analytics.DidNotReceive().TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>());
    }

    // T11: ByLineTitle tests for SubmittedRequestViewModel
    [Theory]
    [InlineData(RequestStatus.Withdrawn, "Submitted by")]
    [InlineData(RequestStatus.NotSubmitted, "Saved by")]
    [InlineData(RequestStatus.InProgress, "Last saved by")]
    [InlineData(RequestStatus.ReadyToSubmit, "Last saved by")]
    [InlineData(RequestStatus.SubmittedUnCommitted, "Submitted by")]
    [InlineData(RequestStatus.SubmittedCommitted, "Submitted by")]
    public void ByLineTitle_ReturnsCorrectTitleForStatus(RequestStatus status, string expected)
    {
        var view = SubmittedRequestViewModelForStatus(status);

        Assert.Equal(expected, view.ByLineTitle);
    }

    [Fact]
    public void ShowReferenceNumber_HidesForWithdrawn()
    {
        var view = SubmittedRequestViewModelForStatus(RequestStatus.Withdrawn);

        Assert.False(view.ShowReferenceNumber);
    }

    [Fact]
    public void ShowReferenceNumber_HidesForNotSubmitted()
    {
        var view = SubmittedRequestViewModelForStatus(RequestStatus.NotSubmitted);

        Assert.False(view.ShowReferenceNumber);
    }

    [Fact]
    public void ShowReferenceNumber_HidesForDraftInConfirmingDelete()
    {
        var view = SubmittedRequestViewModelForStatus(RequestStatus.InProgress, true);

        Assert.False(view.ShowReferenceNumber);
    }

    [Fact]
    public void ShowReferenceNumber_ShowsForDraftNotConfirmingDelete()
    {
        var view = SubmittedRequestViewModelForStatus(RequestStatus.InProgress, false);

        Assert.True(view.ShowReferenceNumber);
    }

    [Fact]
    public void ShowReferenceNumber_ShowsForSubmittedUnCommitted()
    {
        var view = SubmittedRequestViewModelForStatus(RequestStatus.SubmittedUnCommitted);

        Assert.True(view.ShowReferenceNumber);
    }

    [Fact]
    public void ShowReferenceNumber_ShowsForSubmittedCommitted()
    {
        var view = SubmittedRequestViewModelForStatus(RequestStatus.SubmittedCommitted);

        Assert.True(view.ShowReferenceNumber);
    }

    [Fact]
    public void SubmittedAtText_UsesLondonTime_Format()
    {
        var submittedAt = new DateTime(2026, 6, 16, 9, 30, 0);
        var view = SubmittedRequestViewModelForStatus(RequestStatus.SubmittedCommitted, false, submittedAt);

        Assert.Equal(LondonTime.ToSubmittedAtText(submittedAt), view.SubmittedAtText);
    }

    private static SubmittedRequestViewModel SubmittedRequestViewModelForStatus(
        RequestStatus status,
        bool confirmingDelete = false,
        DateTime? submittedAt = null)
    {
        return new SubmittedRequestViewModel
        {
            WindowId = WindowId,
            WhatToChange = WhatToChange.Remove,
            Status = status,
            ConfirmingDelete = confirmingDelete,
            PupilName = "Jane Smith",
            FirstRecordDisplay = null,
            SecondRecordDisplay = null,
            Rows = [],
            Files = [],
            ReferenceNumber = Reference,
            SubmittedByEmail = "test@test.com",
            SubmittedAt = submittedAt
        };
    }

    // T14: ByLineTitle tests for ConfirmDataCorrectViewModel
    [Theory]
    [InlineData(RequestStatus.Withdrawn, "Submitted by")]
    [InlineData(RequestStatus.InProgress, "Last saved by")]
    [InlineData(RequestStatus.ReadyToSubmit, "Last saved by")]
    [InlineData(RequestStatus.SubmittedUnCommitted, "Submitted by")]
    [InlineData(RequestStatus.SubmittedCommitted, "Submitted by")]
    public void ConfirmDataCorrect_ByLineTitle_ReturnsCorrectTitleForStatus(RequestStatus status, string expected)
    {
        var vm = ConfirmDataCorrectViewModelForStatus(status);

        Assert.Equal(expected, vm.ByLineTitle);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowReferenceNumber_HidesForInProgress()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.InProgress);

        Assert.False(vm.ShowReferenceNumber);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowReferenceNumber_HidesForReadyToSubmit()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.ReadyToSubmit);

        Assert.False(vm.ShowReferenceNumber);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowReferenceNumber_HidesForConfirmingDelete()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.SubmittedUnCommitted, true);

        Assert.False(vm.ShowReferenceNumber);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowReferenceNumber_ShowsForSubmittedUnCommitted()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.SubmittedUnCommitted, false);

        Assert.True(vm.ShowReferenceNumber);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowReferenceNumber_ShowsForSubmittedCommitted()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.SubmittedCommitted, false);

        Assert.True(vm.ShowReferenceNumber);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowReferenceNumber_HidesWhenConfirmingDeleteAndHashCodeInProgress()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.InProgress, true);

        Assert.False(vm.ShowReferenceNumber);
    }

    [Fact]
    public void ConfirmDataCorrect_SubmittedAtText_UsesLondonTime_Format()
    {
        var submittedAt = new DateTime(2026, 6, 16, 9, 30, 0);
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.SubmittedCommitted, false, submittedAt);

        Assert.Equal(LondonTime.ToSubmittedAtText(submittedAt), vm.SubmittedAtText);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowDeleteButton_HidesForWithdrawn()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.Withdrawn);

        Assert.False(vm.ShowDeleteButton);
    }

    [Fact]
    public void ConfirmDataCorrect_ShowDeleteButton_ShowsForSubmittedUnCommitted()
    {
        var vm = ConfirmDataCorrectViewModelForStatus(RequestStatus.SubmittedUnCommitted);

        Assert.True(vm.ShowDeleteButton);
    }

    private static ConfirmDataCorrectViewModel ConfirmDataCorrectViewModelForStatus(
        RequestStatus status,
        bool confirmingDelete = false,
        DateTime? submittedAt = null)
    {
        return new ConfirmDataCorrectViewModel
        {
            WindowId = WindowId,
            Status = status,
            ConfirmingDelete = confirmingDelete,
            SubmittedByEmail = "test@test.com",
            SubmittedAt = submittedAt,
            ReferenceNumber = Reference
        };
    }
}
