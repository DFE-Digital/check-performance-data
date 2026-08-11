namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class UrnAmendmentRequestsResult
{
    public required IReadOnlyList<OpenWindow> OpenWindows { get; init; }
    public required IReadOnlyList<UrnAmendmentRequestDto> SubmittedRows { get; init; }
}

public class OpenWindow
{
    public string WindowName { get; init; }
    public Guid WindowId { get; init; }
    public DateTime WindowEndDate { get; init; }
}