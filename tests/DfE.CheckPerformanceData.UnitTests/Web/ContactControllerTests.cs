using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ContactControllerTests
{
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IContentBlockService _contentBlocks = Substitute.For<IContentBlockService>();
    private readonly ILogger<ContactController> _logger = Substitute.For<ILogger<ContactController>>();

    private ContactController CreateSut(bool authenticated, out DefaultHttpContext httpContext)
    {
        httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("localhost");
        var identity = authenticated ? new ClaimsIdentity("TestAuth") : new ClaimsIdentity();
        httpContext.User = new ClaimsPrincipal(identity);
        return new ContactController(_analytics, _currentUser, _contentBlocks, _logger)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>()),
        };
    }

    [Fact]
    public async Task Index_anonymous_shows_reduced_enquiry_list()
    {
        var sut = CreateSut(authenticated: false, out _);
        var vm = Assert.IsType<ContactViewModel>(Assert.IsType<ViewResult>(await sut.Index(null)).Model);
        Assert.False(vm.IsAuthenticated);
        Assert.Equal(3, vm.EnquiryOptions.Count);
    }

    [Fact]
    public async Task Index_signed_in_shows_full_list_and_establishment_context()
    {
        _currentUser.OrganisationName.Returns("Kingsmead School");
        _currentUser.OrganisationLaestab.Returns("860/4070");
        var sut = CreateSut(authenticated: true, out _);
        var vm = Assert.IsType<ContactViewModel>(Assert.IsType<ViewResult>(await sut.Index(null)).Model);
        Assert.True(vm.IsAuthenticated);
        Assert.Equal(4, vm.EnquiryOptions.Count);
        Assert.Equal("Kingsmead School", vm.OrganisationName);
        Assert.Equal("860/4070", vm.OrganisationLaestab);
    }

    [Fact]
    public async Task Index_captures_safe_returnUrl_and_rejects_unsafe_or_contact()
    {
        var sut = CreateSut(authenticated: false, out _);
        Assert.Equal("/guidance/ks4", Model(await sut.Index("/guidance/ks4")).ReturnUrl);
        Assert.Null(Model(await sut.Index("//evil.example")).ReturnUrl);
        Assert.Null(Model(await sut.Index("/contact?returnUrl=/x")).ReturnUrl);
    }

    [Fact]
    public async Task Index_falls_back_to_same_origin_get_referer_excluding_contact()
    {
        var sut = CreateSut(authenticated: false, out var ctx);
        ctx.Request.Headers.Referer = "http://localhost/guidance/ks4";
        Assert.Equal("/guidance/ks4", Model(await sut.Index(null)).ReturnUrl);

        var sut2 = CreateSut(authenticated: false, out var ctx2);
        ctx2.Request.Headers.Referer = "http://evil.example/x";
        Assert.Null(Model(await sut2.Index(null)).ReturnUrl);

        var sut3 = CreateSut(authenticated: false, out var ctx3);
        ctx3.Request.Headers.Referer = "http://localhost/contact";
        Assert.Null(Model(await sut3.Index(null)).ReturnUrl);
    }

    [Fact]
    public async Task Submit_no_selection_reshows_form_and_tracks_validation_error()
    {
        var sut = CreateSut(authenticated: false, out _);
        var view = Assert.IsType<ViewResult>(await sut.Submit(new ContactViewModel { EnquiryType = null }));
        Assert.Equal("Index", view.ViewName);
        Assert.False(sut.ModelState.IsValid);
        await _analytics.Received(1).TrackAsync(
            Arg.Is<ValidationErrorEvent>(e => e.ErrorCount == 1 && e.ErrorCodes.Contains("no_selection")),
            Arg.Any<CancellationToken>());
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<ContactUsSubmittedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_out_of_audience_value_is_rejected()
    {
        var sut = CreateSut(authenticated: false, out _);
        Assert.IsType<ViewResult>(await sut.Submit(new ContactViewModel { EnquiryType = "pupil-data-query" }));
        await _analytics.DidNotReceive().TrackAsync(Arg.Any<ContactUsSubmittedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_valid_tracks_event_logs_and_redirects_to_returnUrl()
    {
        var sut = CreateSut(authenticated: false, out _);
        var result = await sut.Submit(new ContactViewModel { EnquiryType = "technical-problem", ReturnUrl = "/guidance/ks4" });
        Assert.Equal("/guidance/ks4", Assert.IsType<RedirectResult>(result).Url);
        Assert.Equal(true, sut.TempData["ContactUsSubmitted"]);
        await _analytics.Received(1).TrackAsync(
            Arg.Is<ContactUsSubmittedEvent>(e => e.EnquiryType == "technical-problem" && !e.IsAuthenticated),
            Arg.Any<CancellationToken>());
        Assert.Contains(_logger.ReceivedCalls(), (ICall c) => c.GetMethodInfo().Name == "Log");
    }

    [Fact]
    public async Task Submit_valid_with_no_returnUrl_redirects_to_guidance()
    {
        var sut = CreateSut(authenticated: false, out _);
        var result = await sut.Submit(new ContactViewModel { EnquiryType = "general-query", ReturnUrl = null });
        Assert.Equal("/guidance", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task Submit_valid_with_contact_returnUrl_redirects_to_guidance()
    {
        var sut = CreateSut(authenticated: false, out _);
        var result = await sut.Submit(new ContactViewModel { EnquiryType = "general-query", ReturnUrl = "/contact" });
        Assert.Equal("/guidance", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task Submit_no_selection_echoes_back_typed_contact_fields()
    {
        var sut = CreateSut(authenticated: false, out _);
        var result = await sut.Submit(new ContactViewModel
        {
            EnquiryType = null,
            Name = "Ada",
            Email = "ada@example.com",
            School = "Kingsmead",
            Details = "some details",
        });
        var vm = Assert.IsType<ContactViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Ada", vm.Name);
        Assert.Equal("ada@example.com", vm.Email);
        Assert.Equal("Kingsmead", vm.School);
        Assert.Equal("some details", vm.Details);
    }

    [Fact]
    public async Task Submit_valid_with_unsafe_returnUrl_redirects_to_guidance()
    {
        var sut = CreateSut(authenticated: false, out _);
        var result = await sut.Submit(new ContactViewModel { EnquiryType = "general-query", ReturnUrl = "https://evil.example/x" });
        Assert.Equal("/guidance", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public async Task FeedbackLink_tracks_referer_path_only_and_redirects_to_index()
    {
        var sut = CreateSut(authenticated: false, out var ctx);
        ctx.Request.Headers.Referer = "https://host/CheckYourPupilData/x?includedSearch=Smith";

        var result = await sut.FeedbackLink();

        Assert.Equal(nameof(ContactController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        await _analytics.Received(1).TrackAsync(
            Arg.Is<FeedbackClickedEvent>(e => e.PagePath == "/CheckYourPupilData/x"),
            Arg.Any<CancellationToken>());
    }

    private static ContactViewModel Model(IActionResult result) =>
        Assert.IsType<ContactViewModel>(Assert.IsType<ViewResult>(result).Model);
}
