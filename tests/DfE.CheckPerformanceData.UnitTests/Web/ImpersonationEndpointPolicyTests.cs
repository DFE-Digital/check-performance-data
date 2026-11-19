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
    [InlineData("CheckYourPupilData", "NextStep", "POST", true)]
    [InlineData("WhatToChange", "Index", "GET", true)]
    [InlineData("WhatToChange", "Confirm", "POST", true)]
    [InlineData("ResultIssue", "Confirm", "POST", true)]
[InlineData("ResultIssue", "Cancel", "GET", true)]
    [InlineData("Journey", "Page", "GET", true)]
    [InlineData("Journey", "PupilSearchPost", "POST", true)]
    [InlineData("Journey", "UploadFile", "POST", true)]
    [InlineData("Journey", "SummaryConfirm", "POST", true)]
    [InlineData("PupilSuggestions", "Suggestions", "GET", true)]
    [InlineData("ResultSuggestions", "Suggestions", "GET", true)]
    [InlineData("Impersonation", "Start", "POST", false)]
    [InlineData("CheckYourPupilData", "Submit", "POST", false)]
    [InlineData("ConfirmCorrect", "Confirm", "POST", false)]
    [InlineData("AmendmentRequests", "Delete", "POST", false)]
    [InlineData("Journey", "Summary", "POST", false)]
    [InlineData("Admin", "Index", "GET", false)]
    [InlineData("LandingPage", "Index", "DELETE", false)]
    [InlineData("LandingPage", "Create", "POST", false)]
    public void CanView_allows_preview_navigation_and_rejects_other_writes(
        string controller, string action, string method, bool expected)
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action };

        Assert.Equal(expected, ImpersonationEndpointPolicy.CanView(descriptor, method));
    }
}
