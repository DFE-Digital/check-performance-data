namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The result of an ingress run. The ingress summary holds the value as <c>outcome</c>, so a
/// value must not change.
/// </summary>
public static class IngressOutcome
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
