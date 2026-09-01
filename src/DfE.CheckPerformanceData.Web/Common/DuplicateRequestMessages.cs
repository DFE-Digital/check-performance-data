using DfE.CheckPerformanceData.Application.WindowManagement;
using System.Text.Encodings.Web;

namespace DfE.CheckPerformanceData.Web.Common;

public static class DuplicateRequestMessages
{
    private static string TopLevelRequestLabel(string requestCategory, LearnerNoun noun) => requestCategory switch
    {
        "Remove" => $"{noun.Singular} removal request",
        "Include" => $"{noun.Singular} inclusion request",
        "Merge" => $"{noun.Singular} merge request",
        _ => requestCategory.ToLowerInvariant()
    };

    public static string AttentionBannerHtml(
        bool isSelf, bool reasonsMatch, string requestCategory, string pupilName,
        string referenceNumber, string linkUrl, string userName, LearnerNoun noun)
    {
        var enc = HtmlEncoder.Default;
        var topLevelRequest = TopLevelRequestLabel(requestCategory, noun);
        var link = $"<a class=\"govuk-link\" href=\"{enc.Encode(linkUrl)}\" target=\"_blank\" rel=\"noreferrer noopener\">View submitted request (opens in new tab)</a>";

        var encPupilName = enc.Encode(pupilName);
        var encUserName = enc.Encode(userName);
        var encRefNum = enc.Encode(referenceNumber);
        var encTopLevelRequest = enc.Encode(topLevelRequest);

        string message;
        if (isSelf && reasonsMatch)
        {
            message = $"You have already submitted a {encTopLevelRequest} for {encPupilName}. Reference {encRefNum} {link}.";
            message += "<br><br>To raise a new request, delete the previously submitted request. Then return to this page to continue.";
        }
        else if (!isSelf && reasonsMatch)
        {
            message = $"Your colleague {encUserName} has already submitted a {encTopLevelRequest} for {encPupilName}. Reference {encRefNum} {link}.";
            message += "<br><br>To raise a new request, delete the previously submitted request. Then return to this page to continue.";
        }
        else if (isSelf && !reasonsMatch)
        {
            message = $"You have already submitted a request of a different type ({encTopLevelRequest}) for {encPupilName}. Reference {encRefNum} {link}.";
            message += "<br><br>To raise a new request, delete the previously submitted request. Then return to this page to continue.";
        }
        else
        {
            message = $"Your colleague {encUserName} has already submitted a request of a different type ({encTopLevelRequest}) for {encPupilName}. Reference {encRefNum} {link}.";
            message += "<br><br>To raise a new request check with your colleague, and if you want to proceed, delete the previously submitted request. Then return to this page to continue.";
        }

        return message;
    }

    public static string ErrorSummaryMessage(LearnerNoun noun) =>
        $"A request has already been submitted for this {noun.Singular}";

    public static string FieldErrorMessage(LearnerNoun noun) => $"Choose another {noun.Singular}";

    public static string SummaryMessage(bool isSelf, bool reasonsMatch, string requestCategory, LearnerNoun noun)
    {
        var topLevelRequest = TopLevelRequestLabel(requestCategory, noun);

        if (isSelf && reasonsMatch)
            return $"You have already submitted a {topLevelRequest} for this {noun.Singular}.";

        if (!isSelf && reasonsMatch)
            return $"A colleague at your school has already submitted a {topLevelRequest} for this {noun.Singular}.";

        if (isSelf && !reasonsMatch)
            return $"You have already submitted a request of a different type ({topLevelRequest}) for this {noun.Singular}.";

        return $"A colleague at your school has already submitted a request of a different type ({topLevelRequest}) for this {noun.Singular}.";
    }
}
