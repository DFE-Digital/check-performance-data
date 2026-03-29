namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public class DfeSigninSettings
{
    public const string SectionName = "DfeSignIn";
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiClientSecret { get; set; }
    public string ClientId { get; set; }
    public string Audience { get; set; }
    public string? MetadataAddress { get; set; }
    public string? ClientSecret { get; set; }
}