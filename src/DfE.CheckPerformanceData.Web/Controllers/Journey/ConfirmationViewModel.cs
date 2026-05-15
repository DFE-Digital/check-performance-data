namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class ConfirmationViewModel
{
    public Guid WindowId { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? WindowCloseLabel { get; init; }
}
