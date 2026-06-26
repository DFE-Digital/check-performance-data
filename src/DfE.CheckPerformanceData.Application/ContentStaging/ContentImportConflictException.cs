namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Thrown by ImportAsync in Fail mode when the bundle would touch content that already
// exists in the target environment, so the whole import is aborted before any change.
public sealed class ContentImportConflictException(string message) : Exception(message);
