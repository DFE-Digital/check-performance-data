namespace DfE.CheckPerformanceData.Web.Settings;

/// <summary>
/// The home-page notification banner that tells schools this service is a beta and is not the
/// existing checking-exercise site, rendered by <c>_BetaBanner.cshtml</c>. Off by default and
/// switched on per environment in <c>terraform/application/config/{env}.yml</c>
/// </summary>
public record BetaBannerSettings
{
    public bool Enabled { get; init; }
}
