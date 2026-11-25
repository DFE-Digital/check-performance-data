using DfE.CheckPerformanceData.Web.Impersonation;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// #937: the controller guards the request and passes the values to ISampleSchoolService. The
// rules for the values are pinned in SampleSchoolServiceTests.
public sealed class SampleSchoolsControllerTests
{
    private static readonly Guid WindowId = Guid.NewGuid();
    private static readonly Guid SchoolId = Guid.NewGuid();

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();
    private readonly ISampleSchoolService _sampleSchools = Substitute.For<ISampleSchoolService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly EstablishmentDirectory _establishments = Directory();

    // Two made-up schools.
    private static EstablishmentDirectory Directory()
    {
        var directory = new EstablishmentDirectory();
        directory.LoadCsv("""
            URN,LA (code),EstablishmentNumber,EstablishmentName,StatutoryLowAge,StatutoryHighAge
            123456,933,4290,Example Primary School,4,11
            654321,933,4291,Other Academy,11,16
            """);
        return directory;
    }

    private static readonly SampleSchoolInputResult ValidInput = new("9334290", "123456", null, null);

    public SampleSchoolsControllerTests()
    {
        _currentUser.DisplayName.Returns("Ada Admin");
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId, Title = "KS2 September", KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2,
            StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1)
        });
        _sampleSchools.FindAsync(WindowId, SchoolId, Arg.Any<CancellationToken>())
            .Returns(new SampleSchoolDto(SchoolId, WindowId, "9334290", "123456", DateTime.UtcNow, "Ada Admin"));
    }

    private SampleSchoolsController Sut()
    {
        var controller = new SampleSchoolsController(_sampleSchools, _windowService, _currentUser, _establishments)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task A_valid_school_goes_to_the_confirmation_page_and_nothing_is_written()
    {
        _sampleSchools.CheckAddAsync(WindowId, "933/4290", "123456", Arg.Any<CancellationToken>())
            .Returns(new SampleSchoolAddResult(SampleSchoolAddStatus.Ok, ValidInput));

        var result = await Sut().Check(WindowId, new SampleSchoolAddItem { Laestab = "933/4290", Urn = "123456" }, CancellationToken.None);

        var model = Assert.IsType<SampleSchoolConfirmItem>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("9334290", model.Laestab);
        Assert.Equal("933/4290", model.LaestabDisplay);
        Assert.Equal("Example Primary School", model.SchoolName);
        await _sampleSchools.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default!, default);
    }

    [Fact]
    public void Search_finds_schools_by_name_and_gives_only_the_name_urn_and_laestab()
    {
        var result = Assert.IsType<JsonResult>(Sut().Search(WindowId, "example"));

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Equal("""[{"Name":"Example Primary School","Urn":"123456","Laestab":"9334290"}]""", json);
    }

    [Fact]
    public async Task The_add_page_links_to_the_windows_school_search()
    {
        var result = await Sut().Add(WindowId, CancellationToken.None);

        var model = Assert.IsType<SampleSchoolAddItem>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal($"/admin/windows/{WindowId}/sample-schools/search", model.SearchUrl);
    }

    [Fact]
    public async Task A_bad_value_shows_the_form_again_with_the_error_on_its_field()
    {
        _sampleSchools.CheckAddAsync(WindowId, "abc", "1", Arg.Any<CancellationToken>())
            .Returns(new SampleSchoolAddResult(SampleSchoolAddStatus.Invalid,
                new SampleSchoolInputResult(null, null, SampleSchoolInput.LaestabInvalid, SampleSchoolInput.UrnInvalid)));
        var controller = Sut();

        var result = await controller.Check(WindowId, new SampleSchoolAddItem { Laestab = "abc", Urn = "1" }, CancellationToken.None);

        Assert.IsType<SampleSchoolAddItem>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(SampleSchoolInput.LaestabInvalid, controller.ModelState[nameof(SampleSchoolAddItem.Laestab)]!.Errors.Single().ErrorMessage);
        Assert.Equal(SampleSchoolInput.UrnInvalid, controller.ModelState[nameof(SampleSchoolAddItem.Urn)]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task A_school_already_on_the_list_is_an_error_on_the_laestab()
    {
        _sampleSchools.CheckAddAsync(WindowId, "9334290", "123456", Arg.Any<CancellationToken>())
            .Returns(new SampleSchoolAddResult(SampleSchoolAddStatus.AlreadyListed, ValidInput));
        var controller = Sut();

        await controller.Check(WindowId, new SampleSchoolAddItem { Laestab = "9334290", Urn = "123456" }, CancellationToken.None);

        Assert.Single(controller.ModelState[nameof(SampleSchoolAddItem.Laestab)]!.Errors);
    }

    [Fact]
    public async Task Confirm_adds_the_school_as_the_signed_in_admin_and_goes_back_to_the_window()
    {
        _sampleSchools.AddAsync(WindowId, "9334290", "123456", "Ada Admin", Arg.Any<CancellationToken>())
            .Returns(new SampleSchoolAddResult(SampleSchoolAddStatus.Ok, ValidInput));
        var controller = Sut();

        var result = await controller.Confirm(WindowId, new SampleSchoolAddItem { Laestab = "9334290", Urn = "123456" }, CancellationToken.None);

        Assert.Equal($"/admin/windows/summary/{WindowId}", Assert.IsType<RedirectResult>(result).Url);
        Assert.Contains("933/4290", (string)controller.TempData[SampleSchoolsController.TempDataKey]!);
    }

    [Fact]
    public async Task An_unknown_window_is_not_found()
    {
        var unknown = Guid.NewGuid();

        Assert.IsType<NotFoundResult>(await Sut().Add(unknown, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Sut().Confirm(unknown, new SampleSchoolAddItem(), CancellationToken.None));
        await _sampleSchools.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task Remove_shows_a_confirmation_page_first()
    {
        var result = await Sut().Remove(WindowId, SchoolId, CancellationToken.None);

        var model = Assert.IsType<SampleSchoolRemoveItem>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("933/4290", model.LaestabDisplay);
        await _sampleSchools.DidNotReceiveWithAnyArgs().RemoveAsync(default, default, default);
    }

    [Fact]
    public async Task Remove_confirmed_removes_the_school_and_goes_back_to_the_window()
    {
        _sampleSchools.RemoveAsync(WindowId, SchoolId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Sut().RemoveConfirmed(WindowId, SchoolId, CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
        await _sampleSchools.Received(1).RemoveAsync(WindowId, SchoolId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_of_a_school_that_another_window_lists_is_not_found()
    {
        var otherWindow = Guid.NewGuid();

        Assert.IsType<NotFoundResult>(await Sut().RemoveConfirmed(otherWindow, SchoolId, CancellationToken.None));
        await _sampleSchools.DidNotReceiveWithAnyArgs().RemoveAsync(default, default, default);
    }
}
