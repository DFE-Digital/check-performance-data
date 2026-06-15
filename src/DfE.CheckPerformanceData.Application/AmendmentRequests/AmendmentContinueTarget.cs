namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public abstract record AmendmentContinueTarget;

public sealed record ContinueToSummary : AmendmentContinueTarget;

public sealed record ContinueToPage(string PageId) : AmendmentContinueTarget;
