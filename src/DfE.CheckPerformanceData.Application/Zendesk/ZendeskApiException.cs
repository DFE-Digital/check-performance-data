namespace DfE.CheckPerformanceData.Application.ZendeskClient;

/// <summary>
/// Represents an error that occurred while communicating with the Zendesk API.
/// </summary>
public class ZendeskApiException : Exception
{
    public int? HttpStatusCode { get; }
    public string? Operation { get; }

    public ZendeskApiException(string message)
        : base(message)
    {
    }

    public ZendeskApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ZendeskApiException(string message, int? httpStatusCode, string operation)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        Operation = operation;
    }

    public ZendeskApiException(string message, int? httpStatusCode, string operation, Exception innerException)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        Operation = operation;
    }
}