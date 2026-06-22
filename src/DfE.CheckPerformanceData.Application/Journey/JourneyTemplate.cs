namespace DfE.CheckPerformanceData.Application.Journey;

public static class JourneyTemplate
{
    public static string Resolve(string template, string pupilName) =>
        template.Replace("{pupilName}", pupilName, StringComparison.Ordinal);

    /// <summary>
    /// Produces a pupil-name-free version of a title for use in the browser
    /// &lt;title&gt; (and therefore analytics). The pupil-name token is neutralised
    /// to "the pupil" rather than blanked so the result stays grammatical.
    /// </summary>
    public static string Strip(string template) =>
        template
            .Replace("{pupilName}'s", "the pupil's", StringComparison.Ordinal)
            .Replace("{pupilName}", "the pupil", StringComparison.Ordinal);
}
