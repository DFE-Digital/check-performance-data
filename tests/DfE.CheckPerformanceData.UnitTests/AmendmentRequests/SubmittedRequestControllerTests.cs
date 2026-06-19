using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class SubmittedRequestControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const string Reference = "CYPMD_KS4June_ABC1234";

    private readonly ISubmittedRequestService _service = Substitute.For<ISubmittedRequestService>();
    private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
    private readonly SubmittedRequestController _sut;

    public SubmittedRequestControllerTests()
    {
        _sut = new SubmittedRequestController(_service, _fileStorage);
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
    public void Delete_RedirectsBackToView()
    {
        var result = _sut.Delete(WindowId, Reference);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("View", redirect.ActionName);
        Assert.Equal(WindowId, redirect.RouteValues!["windowId"]);
        Assert.Equal(Reference, redirect.RouteValues!["referenceNumber"]);
    }
}
