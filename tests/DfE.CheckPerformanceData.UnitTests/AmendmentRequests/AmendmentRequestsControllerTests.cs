using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public class AmendmentRequestsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IAmendmentRequestsService _service = Substitute.For<IAmendmentRequestsService>();
    private readonly AmendmentRequestsController _sut;

    public AmendmentRequestsControllerTests()
    {
        _sut = new AmendmentRequestsController(_service);
    }

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
            Rows = []
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
                    RequestType = "Remove - Permanently left England",
                    Status = RequestStatus.ReadyToSubmit,
                    ReferenceNumber = "REF001"
                }
            ]
        });

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Single(vm.Rows);
        Assert.Equal("Jane Smith", vm.Rows[0].PupilName);
        Assert.Equal("Remove - Permanently left England", vm.Rows[0].RequestType);
        Assert.Equal(RequestStatus.ReadyToSubmit, vm.Rows[0].Status);
        Assert.Equal("REF001", vm.Rows[0].ReferenceNumber);
    }

    [Fact]
    public async Task Index_SetsWindowId()
    {
        _service.GetAmendmentRequestsAsync(WindowId).Returns(EmptyResult());

        var result = await _sut.Index(WindowId);

        var vm = Assert.IsType<AmendmentRequestsViewModel>(((ViewResult)result).Model);
        Assert.Equal(WindowId, vm.WindowId);
    }

    private static AmendmentRequestsResult EmptyResult() => new()
    {
        WindowEndDate = new DateTime(2026, 6, 26, 17, 0, 0),
        Rows = []
    };
}
