namespace DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;

public sealed class BulkConfirmationViewModel
{
    public required Guid WindowId { get; init; }
    public required IReadOnlyList<string> ReferenceNumbers { get; init; }
    /// <summary>
    /// The pupil-data exercise's close, formatted for display. Null when the window runs no pupil
    /// data checking — the "you still have until" banner is then dropped rather than left blank.
    /// </summary>
    public required string? WindowCloseLabel { get; init; }
}
