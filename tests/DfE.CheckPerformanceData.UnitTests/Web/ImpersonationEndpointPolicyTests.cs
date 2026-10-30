using Microsoft.AspNetCore.Mvc.Controllers;
using DfE.CheckPerformanceData.Web.Impersonation;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ImpersonationEndpointPolicyTests
{
    [Theory]
    [InlineData("LandingPage", "Index", "GET", true)]
    [InlineData("LandingPage", "Index", "POST", true)]
    [InlineData("CheckYourPupilData", "Index", "POST", true)]
    [InlineData("AmendmentRequests", "Index", "POST", true)]
    [InlineData("Impersonation", "Exit", "POST", true)]
    [InlineData("Impersonation", "Start", "POST", false)]
    [InlineData("CheckYourPupilData", "Submit", "POST", false)]
    [InlineData("Admin", "Index", "GET", false)]
    [InlineData("LandingPage", "Index", "DELETE", false)]
    [InlineData("LandingPage", "Create", "POST", false)]
    public void CanView_only_allows_explicit_read_routes_and_safe_methods(
        string controller, string action, string method, bool expected)
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action };

        Assert.Equal(expected, ImpersonationEndpointPolicy.CanView(descriptor, method));
    }
}
