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
        "WhatToChange.Index", "ConfirmCorrect.Index", "ResultIssue.Index", "ResultIssue.Cancel",
        "Journey.Page", "Journey.PupilSearchPage", "Journey.ResultSearchPage",
        "Journey.QualificationSearchPage", "Journey.Summary", "Journey.DownloadEvidence",
        "Journey.EnquiryConfirmation", "Journey.Confirmation", "Journey.DuplicateCheck",
        "Journey.PupilNotFound", "Journey.AlreadyIncluded", "Journey.PupilSearchAgain",
        "PupilSuggestions.Suggestions", "ResultSuggestions.Suggestions", "Country.Suggestions",
        "AmendmentRequests.Index", "AmendmentRequests.DraftDetails",
        "EstablishmentAmendmentRequests.Index", "EstablishmentAmendmentRequests.View", "SubmittedRequest.View", "SubmittedRequest.ViewConfirmation",
        "SubmittedRequest.DraftDetails", "SubmittedRequest.DownloadEvidence",
        "Impersonation.Exit", "DfeSignOut.Index"
    };

    private static readonly HashSet<string> PreviewInteractions = new(StringComparer.Ordinal)
    {
        "CheckYourPupilData.NextStep", "WhatToChange.Confirm", "ResultIssue.Confirm",
        "Journey.PupilSearchPost", "Journey.ResultSearchPost",
        "Journey.QualificationSearchPost", "Journey.PagePost",
        "Journey.UploadFile", "Journey.RemoveFile",
        "Journey.SummaryConfirm", "Journey.SaveDraft",
        "Journey.ContinueAdding", "Journey.AbortDuplicateCheck",
        "Journey.IncludeThisPupil", "Journey.SwitchToInclude",
        "Journey.StartAddingPupil", "Journey.AbortInclude"
    };

    private static readonly HashSet<string> ReadOnlyPosts = new(StringComparer.Ordinal)
    {
        "LandingPage.Index", "CheckYourPupilData.Index", "AmendmentRequests.Index", "Impersonation.Exit"
    };

    public static bool CanView(ControllerActionDescriptor action, string method)
    {
        var key = action.ControllerName + "." + action.ActionName;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            return Reads.Contains(key);

        if (!HttpMethods.IsPost(method)) return false;
        return PreviewInteractions.Contains(key) || ReadOnlyPosts.Contains(key);
    }
}
