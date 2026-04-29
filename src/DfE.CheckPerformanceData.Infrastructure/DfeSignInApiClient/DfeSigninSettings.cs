namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public class DfeSigninSettings
{
    public const string SectionName = "DfeSignIn";
    public string BaseUrl { get; init; } = string.Empty;
    public required string ApiClientSecret { get; init; }
    public required string ClientId { get; init; }
    public required string Audience { get; init; }
    public string? MetadataAddress { get; init; }
    public string? ClientSecret { get; init; }
    public string? ServiceId { get; init; }
}