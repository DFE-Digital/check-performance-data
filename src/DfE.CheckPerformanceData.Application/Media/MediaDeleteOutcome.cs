namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>The outcome of deleting a library image.</summary>
public enum MediaDeleteOutcome
{
    Deleted,
    NotFound,
    InUse
}
