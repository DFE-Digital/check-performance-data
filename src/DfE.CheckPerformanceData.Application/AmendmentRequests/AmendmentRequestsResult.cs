namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentRequestsResult
{
    public required DateTime WindowEndDate { get; init; }
    public required string WindowTitle { get; init; }
    public required IReadOnlyList<AmendmentRequestDto> Rows { get; init; }
    public required IReadOnlyList<SubmittedRequestDto> SubmittedRows { get; init; }
}
