using System.Reflection;
using DfE.CheckPerformanceData.Infrastructure.Ingress;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class IngressConstantsTests
{
    private static Dictionary<string, string> ConstantsOf(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Where(field => field.IsLiteral)
        .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

    [Fact]
    public void EachFailureStage_HasTheValueThatTheSummaryHeldBeforeItHadAName()
    {
        var expected = new Dictionary<string, string>
        {
            ["Configuration"] = "configuration",
            ["InputRead"] = "input_read",
            ["Checksum"] = "checksum",
            ["Schema"] = "schema",
            ["Parsing"] = "parsing",
            ["Validation"] = "validation",
            ["DataWrite"] = "data_write",
            ["Publication"] = "publication",
            ["Cancelled"] = "cancelled"
        };

        Assert.Equal(expected, ConstantsOf(typeof(IngressFailureStage)));
    }

    [Fact]
    public void EachFailureCode_HasTheValueThatTheSummaryHeldBeforeItHadAName()
    {
        var expected = new Dictionary<string, string>
        {
            ["NoDatasets"] = "no_datasets",
            ["RequiredFileMissing"] = "required_file_missing",
            ["StorageNotConfigured"] = "storage_not_configured",
            ["InputNotFound"] = "input_not_found",
            ["SchemaNotFound"] = "schema_not_found",
            ["InputReadFailed"] = "input_read_failed",
            ["SchemaReadFailed"] = "schema_read_failed",
            ["ChecksumMismatch"] = "checksum_mismatch",
            ["SchemaInvalid"] = "schema_invalid",
            ["IdentifierMappingInvalid"] = "identifier_mapping_invalid",
            ["IncludableNotDeclared"] = "includable_not_declared",
            ["MalformedHeader"] = "malformed_header",
            ["MalformedRow"] = "malformed_row",
            ["RecordValidationFailed"] = "record_validation_failed",
            ["DataWriteFailed"] = "data_write_failed",
            ["PublicationFailed"] = "publication_failed",
            ["OperationCancelled"] = "operation_cancelled"
        };

        Assert.Equal(expected, ConstantsOf(typeof(IngressFailureCode)));
    }
}
