namespace DfE.CheckPerformanceData.Application.ZendeskClient;

/// <summary>
/// Represents an error that occurred while communicating with the Zendesk API.
/// </summary>
public class ZendeskApiException : Exception
{
    public ZendeskApiException(string message)
        : base(message)
    {
    }

    public ZendeskApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
