namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public sealed class DuplicateRequestException()
    : Exception("A request for this pupil already exists for this checking window.");
