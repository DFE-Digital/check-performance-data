namespace DfE.CheckPerformanceData.Application.Notify;

public interface IEmailLinkGenerator
{
    string GenerateLink(string controller, string action, object? routeValues, string campaignName);
}
