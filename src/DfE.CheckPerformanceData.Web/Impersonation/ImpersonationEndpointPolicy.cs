using Microsoft.AspNetCore.Mvc.Controllers;

namespace DfE.CheckPerformanceData.Web.Impersonation;

/// <summary>Explicit read inventory. New endpoints are denied while impersonating until reviewed.</summary>
public static class ImpersonationEndpointPolicy
{
    private static readonly HashSet<string> Reads = new(StringComparer.Ordinal)
    {
        "LandingPage.Index", "Home.Index", "Home.Error", "Home.NotFound",
        "CheckYourPupilData.Index", "CheckYourPupilData.DownloadAll", "CheckYourPupilData.DownloadIncluded",
        "CheckYourPupilData.DownloadNonIncluded", "CheckYourPupilData.DownloadResults",
        "AmendmentRequests.Index", "AmendmentRequests.DraftDetails",
        "EstablishmentAmendmentRequests.Index", "EstablishmentAmendmentRequests.View", "SubmittedRequest.View", "SubmittedRequest.ViewConfirmation",
        "SubmittedRequest.DraftDetails", "SubmittedRequest.DownloadEvidence",
        "Impersonation.Exit", "DfeSignOut.Index"
    };

    public static bool CanView(ControllerActionDescriptor action, string method) =>
        Reads.Contains(action.ControllerName + "." + action.ActionName)
        && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsPost(method) && (action.ControllerName == "Impersonation" && action.ActionName == "Exit"
                || action.ActionName == "Index" && action.ControllerName is "LandingPage" or "CheckYourPupilData" or "AmendmentRequests"));
}
