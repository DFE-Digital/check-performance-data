using System.Text.Encodings.Web;

namespace DfE.CheckPerformanceData.Web.Common;

public static class DuplicateRequestMessages
{
    private static string TopLevelRequestLabel(string requestCategory) => requestCategory switch
    {
        "Remove" => "pupil removal request",
        "Include" => "pupil inclusion request",
        "Merge" => "pupil merge request",
        _ => requestCategory.ToLowerInvariant()
    };

    public static string AttentionBannerHtml(
        bool isSelf, bool reasonsMatch, string requestCategory, string pupilName,
        string referenceNumber, string linkUrl, string userName)
    {
        var enc = HtmlEncoder.Default;
        var topLevelRequest = TopLevelRequestLabel(requestCategory);
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

    public static string ErrorSummaryMessage => "A request has already been submitted for this pupil";

    public static string FieldErrorMessage => "Choose another pupil";

    public static string SummaryMessage(bool isSelf, bool reasonsMatch, string requestCategory, string userName)
    {
        var topLevelRequest = TopLevelRequestLabel(requestCategory);

        if (isSelf && reasonsMatch)
            return $"You have already submitted a {topLevelRequest} for this pupil.";

        if (!isSelf && reasonsMatch)
            return $"A colleague at your school has already submitted a {topLevelRequest} for this pupil.";

        if (isSelf && !reasonsMatch)
            return $"You have already submitted a request of a different type ({topLevelRequest}) for this pupil.";

        return $"A colleague at your school has already submitted a request of a different type ({topLevelRequest}) for this pupil.";
    }

    public static bool ShowLink() => true;
}
