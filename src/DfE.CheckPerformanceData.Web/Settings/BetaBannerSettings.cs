namespace DfE.CheckPerformanceData.Web.Settings;

/// <summary>
/// The home-page notification banner that tells schools this service is a beta and is not the
/// existing checking-exercise site, rendered by <c>_BetaBanner.cshtml</c>. Off by default and
/// switched on per environment in <c>terraform/application/config/{env}.yml</c>
/// (<c>BetaBanner__Enabled</c>). The sentence, link text and link target are config too, so
/// the wording can follow whichever exercise the existing site is running without a release.
/// </summary>
public record BetaBannerSettings
{
    public bool Enabled { get; init; }

    /// <summary>The sentence that precedes the link. Rendered as text, then the link, then a full stop.</summary>
    public string Message { get; init; } =
        "It is not the current site for the key stage 2 checking exercise. To take part in the KS2 checking exercise, go to the";

    public string ExistingSiteUrl { get; init; } =
        "https://onlinecollections.des.fasst.org.uk/fastform/school-checking-exercise-ks2";

    public string ExistingSiteLinkText { get; init; } = "school checking exercise for key stage 2";
}
