namespace DfE.CheckPerformanceData.Application.Media.Serving;

public enum MediaDeliveryOutcome
{
    /// <summary>The file or rendition is ready to send.</summary>
    Found,

    /// <summary>No such image or version, or its file is missing.</summary>
    NotFound,

    /// <summary>The settings are not allowed, or their signature does not match.</summary>
    Invalid
}
