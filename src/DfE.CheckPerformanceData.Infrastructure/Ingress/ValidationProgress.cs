namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// A single step in the CSV validation run, streamed to the client as it happens.
/// One of these is emitted per stage (checksums, counting, per-group processing) and a
/// final terminal event carries <see cref="IsComplete"/>. A terminal event with
/// <see cref="IsError"/> means the run stopped without producing a clean result.
/// </summary>
public sealed record ValidationProgress(
    string Phase,
    string Message,
    int RecordsRead,
    int RecordsProcessed,
    int FilesWritten,
    int ErrorCount,
    bool IsComplete,
    bool IsError);
