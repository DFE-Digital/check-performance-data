namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// The reason an ingress run stops. The ingress summary holds the value as
/// <c>failure.code</c>, so a value must not change.
/// </summary>
internal static class IngressFailureCode
{
    public const string NoDatasets = "no_datasets";
    public const string RequiredFileMissing = "required_file_missing";
    public const string StorageNotConfigured = "storage_not_configured";
    public const string InputNotFound = "input_not_found";
    public const string SchemaNotFound = "schema_not_found";
    public const string InputReadFailed = "input_read_failed";
    public const string SchemaReadFailed = "schema_read_failed";
    public const string ChecksumMismatch = "checksum_mismatch";
    public const string SchemaInvalid = "schema_invalid";
    public const string IdentifierMappingInvalid = "identifier_mapping_invalid";
    public const string IncludableNotDeclared = "includable_not_declared";
    public const string MalformedHeader = "malformed_header";
    public const string MalformedRow = "malformed_row";
    public const string RecordValidationFailed = "record_validation_failed";
    public const string DataWriteFailed = "data_write_failed";
    public const string PublicationFailed = "publication_failed";
    public const string OperationCancelled = "operation_cancelled";
}
