namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The error code of a log line about the ingress summary. Only the log holds the value. The
/// summary does not. A log search can name the value, so a value must not change.
/// </summary>
internal static class IngressSummaryLogCode
{
    public const string SummaryWriteFailed = "summary_write_failed";
    public const string SummaryClearFailed = "summary_clear_failed";
}
