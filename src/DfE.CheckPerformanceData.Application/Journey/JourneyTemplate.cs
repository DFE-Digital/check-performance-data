namespace DfE.CheckPerformanceData.Application.Journey;

public static class JourneyTemplate
{
    public static string Resolve(string template, string pupilName) =>
        template.Replace("{pupilName}", pupilName, StringComparison.Ordinal);
}
