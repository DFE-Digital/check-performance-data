namespace DfE.CheckPerformanceData.Web.Settings;

/// <summary>
/// Where the phase banner's "feedback (opens in new tab)" link sends people. The anchor itself
/// points at <c>/feedback-link</c> so <c>ContactController.FeedbackLink</c> can record the click
/// as a <c>feedback_clicked</c> analytics event before redirecting here. The survey is a
/// Microsoft Forms page. The default is the live survey; an environment can point at a
/// different one with <c>FeedbackSurvey__Url</c> in <c>terraform/application/config/{env}.yml</c>
/// without a release.
/// </summary>
public record FeedbackSurveySettings
{
    public const string SectionName = "FeedbackSurvey";

    public string Url { get; init; } = "https://forms.cloud.microsoft/e/NtJTefhXHz";
}
