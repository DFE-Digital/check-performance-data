namespace DfE.CheckPerformanceData.Domain.Enums;

public enum RequestType
{
    Amendment,
    ConfirmCorrect,
    // AB#296648: a 16-19 results enquiry (e.g. an incorrect grade report). Stored in the
    // ChangeRequest.RequestType string column (max 20) — "ResultsEnquiry" fits, no migration.
    // Appended last so existing stored values are unmoved.
    ResultsEnquiry
}
