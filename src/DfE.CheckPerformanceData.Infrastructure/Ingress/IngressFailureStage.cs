namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The stage at which an ingress run stops. The ingress summary holds the value as
/// <c>failure.stage</c>, so a value must not change.
/// </summary>
internal static class IngressFailureStage
{
    public const string Configuration = "configuration";
    public const string InputRead = "input_read";
    public const string Checksum = "checksum";
    public const string Schema = "schema";
    public const string Parsing = "parsing";
    public const string Validation = "validation";
    public const string DataWrite = "data_write";
    public const string Publication = "publication";
    public const string Cancelled = "cancelled";
}
