namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public class DfeSigninSettings
{
    public const string SectionName = "DfeSignIn";
    public required string BaseUrl { get; init; }
    public required string ApiClientSecret { get; init; }
    public required string ClientId { get; init; }
    public required string Audience { get; init; }
    public required string MetadataAddress { get; init; }
    public required string ClientSecret { get; init; }
}