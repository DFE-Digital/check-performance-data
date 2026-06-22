namespace DfE.CheckPerformanceData.Application.Observability;

// The allowed time-bucket sizes for throughput aggregation. The date_trunc unit (and the
// floor arithmetic for the derived 5/10-minute buckets) is chosen from this enum and never
// from a user-supplied string, so the raw aggregation SQL has no value-interpolation path.
public enum ThroughputGranularity
{
    Second,
    Minute,
    FiveMinute,
    TenMinute,
    Hour,
    Day,
}
