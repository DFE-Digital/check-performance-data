namespace DfE.CheckPerformanceData.Infrastructure.Authentication;

/// <summary>Internal request-only binding; never a claim, browser input or logged credential.</summary>
public static class AuthenticatedSessionBinding
{
    public static readonly object ItemKey = new();
}
