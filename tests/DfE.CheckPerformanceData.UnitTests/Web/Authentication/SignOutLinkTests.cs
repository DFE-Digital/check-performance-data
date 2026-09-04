using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Authentication;

// AB#298317: the sign-out target used to live only in _Layout. Check your pupil data now signs a
// school out from its "No, I'd like to sign out" answer, so the decision is shared. The rules are
// the layout's: any authenticated identity that is not the dev-impersonation scheme is real DfE
// auth and goes through DfE sign-out; an impersonation-only session clears its cookie instead
// (never /dev/impersonate/user, which kept a synthetic principal signed in).
public sealed class SignOutLinkTests
{
    private readonly IUrlHelper _url = Substitute.For<IUrlHelper>();

    public SignOutLinkTests()
    {
        _url.Action(Arg.Is<UrlActionContext>(c => c.Action == "DfeSignOut" && c.Controller == "Account"))
            .Returns("/Account/DfeSignOut");
    }

    private static HttpContext ContextFor(params ClaimsIdentity[] identities) =>
        new DefaultHttpContext { User = new ClaimsPrincipal(identities) };

    [Fact]
    public void Real_dfe_auth_goes_through_dfe_sign_out()
    {
        var context = ContextFor(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Jo")], "Cookies"));

        Assert.Equal("/Account/DfeSignOut", SignOutLink.For(context, _url));
    }

    [Fact]
    public void Real_auth_is_any_non_impersonation_identity_not_a_named_scheme()
    {
        // DfE Sign-In adds an enrichment identity ("DfeSignIn"); either identity counts.
        var context = ContextFor(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "Jo")], "DfeSignIn"));

        Assert.Equal("/Account/DfeSignOut", SignOutLink.For(context, _url));
    }

    [Fact]
    public void Impersonation_only_clears_the_cookie()
    {
        var context = ContextFor(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "dev")], DevImpersonationConstants.Scheme));

        Assert.Equal("/dev/impersonate/clear", SignOutLink.For(context, _url));
        Assert.Equal(SignOutLink.ImpersonationClearPath, SignOutLink.For(context, _url));
    }

    [Fact]
    public void Impersonation_layered_on_real_auth_still_signs_out_of_dfe()
    {
        var context = ContextFor(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "Jo")], "Cookies"),
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "dev")], DevImpersonationConstants.Scheme));

        Assert.Equal("/Account/DfeSignOut", SignOutLink.For(context, _url));
    }

    [Fact]
    public void Anonymous_falls_back_to_clearing_the_cookie()
    {
        Assert.Equal("/dev/impersonate/clear", SignOutLink.For(new DefaultHttpContext(), _url));
    }

    // AB#298317 review F6: the class predates the returnUrl parameter added for Check your pupil
    // data's sign-out answer — neither branch was pinned against it.

    [Fact]
    public void A_return_url_is_ignored_on_the_real_auth_branch()
    {
        var context = ContextFor(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Jo")], "Cookies"));

        Assert.Equal("/Account/DfeSignOut", SignOutLink.For(context, _url, returnUrl: "/"));
    }

    [Fact]
    public void A_return_url_is_appended_to_the_impersonation_path()
    {
        var context = ContextFor(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "dev")], DevImpersonationConstants.Scheme));

        Assert.Equal("/dev/impersonate/clear?returnUrl=%2F", SignOutLink.For(context, _url, returnUrl: "/"));
    }
}
