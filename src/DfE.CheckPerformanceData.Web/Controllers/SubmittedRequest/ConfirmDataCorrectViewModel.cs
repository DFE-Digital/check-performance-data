namespace DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;

public sealed class ConfirmDataCorrectViewModel
{
    public required Guid WindowId { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public required string ReferenceNumber { get; init; }

    public string RequestTypeDisplay => "Confirm pupil data is correct";

    public string SubmittedAtText =>
        SubmittedAt is { } d ? $"{d:d MMMM yyyy} at {d.ToString("h:mmtt").ToLower()}" : "";
}
