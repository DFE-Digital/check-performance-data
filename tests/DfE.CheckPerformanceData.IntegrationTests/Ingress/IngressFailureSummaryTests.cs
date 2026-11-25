using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

[Collection(nameof(AzuriteCollection))]
public sealed class IngressFailureSummaryTests(AzuriteFixture fixture)
{
    private const string CsvFile = "pupils.csv";
    private const string SchemaFile = "pupils.json";
    private const string Csv = "LAESTAB,SURNAME\n8604070,Smith\n";
    private const string NoRules = "{}";

    private static readonly string Schema = SchemaWith(string.Empty, NoRules);

    private static string SchemaWith(string moreProperties, string ingress) => $$"""
    {
      "type": "object",
      "x-ingress": {{ingress}},
      "properties": {
        "LAESTAB": { "type": ["string", "null"] },
        "SURNAME": { "type": ["string", "null"] }{{moreProperties}}
      }
    }
    """;

    public static TheoryData<string, string, bool?, string, string> FailuresOfASuppliedPair => new()
    {
        { Csv, "{ not a schema", null, "schema", "schema_invalid" },
        {
            Csv, SchemaWith(string.Empty, """{ "inclusion": { "field": "MISSING", "included": ["1"] } }"""),
            null, "schema", "identifier_mapping_invalid"
        },
        {
            Csv,
            SchemaWith(
                """
                ,
                "P_INCL": { "type": ["string", "null"] },
                "P_INCL_DESC": { "type": ["string", "null"] },
                "INCLUDED": { "type": "boolean" },
                "INCLUDABLE": { "type": "boolean" }
                """,
                """{ "inclusion": { "field": "P_INCL", "descriptionField": "P_INCL_DESC", "descriptions": { "1": "Words" } } }"""),
            null, "schema", "identifier_mapping_invalid"
        },
        {
            Csv, SchemaWith(string.Empty, """{ "codeDescriptions": "words" }"""),
            null, "schema", "identifier_mapping_invalid"
        },
        {
            Csv, SchemaWith(string.Empty, """{ "includable": { "field": "MISSING", "codes": ["1"] } }"""),
            null, "schema", "identifier_mapping_invalid"
        },
        {
            Csv, SchemaWith(""", "INCLUDED": { "type": "boolean" }""", NoRules),
            true, "schema", "includable_not_declared"
        },
        { "SCHOOL,SURNAME\n8604070,Smith\n", Schema, null, "schema", "identifier_mapping_invalid" },
        { string.Empty, Schema, null, "parsing", "malformed_header" },
        { "LAESTAB,LAESTAB\n8604070,8604070\n", Schema, null, "parsing", "malformed_header" },
        { "LAESTAB,SURNAME\n8604070\n", Schema, null, "parsing", "malformed_row" },
        { "LAESTAB,SURNAME\n,\n", Schema, null, "parsing", "malformed_row" },
        { "LAESTAB,SURNAME\n8604070,Sm\"ith\n", Schema, null, "parsing", "malformed_row" },
        { "LAESTAB,SURNAME\n", Schema, null, "validation", "record_validation_failed" },
        {
            Csv, SchemaWith(""", "FORENAME": { "type": "string", "minLength": 1 }""", NoRules),
            null, "validation", "record_validation_failed"
        }
    };

    [Theory]
    [MemberData(nameof(FailuresOfASuppliedPair))]
    public async Task ASuppliedPairThatCannotBeIngested_WritesItsStageAndCode(
        string csv, string schema, bool? included, string stage, string code)
    {
        var capture = await RunAsync(csv, schema, included: included);

        AssertFailure(capture, stage, code);
    }

    [Fact]
    public async Task ARunWithNoDatasets_WritesAConfigurationFailure()
    {
        var capture = new IngressSummaryCapture(Guid.NewGuid(), Guid.NewGuid(), [], DateTime.UtcNow);

        await DrainAsync(Processor(Client()), Guid.NewGuid(), [], capture);

        AssertFailure(capture, "configuration", "no_datasets");
    }

    [Fact]
    public async Task ARunWithNoStorageClient_WritesAConfigurationFailure()
    {
        IReadOnlyList<IngressDataset> datasets = [Dataset(Csv, Schema)];
        var capture = new IngressSummaryCapture(Guid.NewGuid(), Guid.NewGuid(), datasets, DateTime.UtcNow);
        var processor = new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
            new Dictionary<string, BlobServiceClient>(), new DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData.SampleDataGenerators([]));

        await DrainAsync(processor, Guid.NewGuid(), datasets, capture);

        AssertFailure(capture, "configuration", "storage_not_configured");
    }

    [Fact]
    public async Task AnInputFileThatIsNotInStorage_WritesAnInputReadFailure() =>
        AssertFailure(await RunAsync(csv: null, Schema), "input_read", "input_not_found");

    [Fact]
    public async Task ASchemaFileThatIsNotInStorage_WritesAnInputReadFailure() =>
        AssertFailure(await RunAsync(Csv, schema: null), "input_read", "schema_not_found");

    [Fact]
    public async Task AnInputFileThatStorageCannotRead_WritesAnInputReadFailure() =>
        AssertFailure(
            await RunAsync(Csv, Schema, FailingWhen(request => request.Uri.Path.Contains("/ingress/"))),
            "input_read", "input_read_failed");

    [Fact]
    public async Task ASchemaFileThatStorageCannotRead_WritesAnInputReadFailure() =>
        AssertFailure(
            await RunAsync(Csv, Schema, FailingWhen(request => request.Uri.Path.Contains("/schema/"))),
            "input_read", "schema_read_failed");

    [Fact]
    public async Task AFileThatDoesNotMatchItsChecksum_WritesAChecksumFailure() =>
        AssertFailure(await RunAsync(Csv, Schema, csvChecksum: "00"), "checksum", "checksum_mismatch");

    [Fact]
    public async Task ADataFileThatStorageCannotWrite_WritesADataWriteFailure() =>
        AssertFailure(
            await RunAsync(Csv, Schema, FailingWhen(request =>
                request.Method == RequestMethod.Put && request.Uri.Path.Contains("/data/"))),
            "data_write", "data_write_failed");

    private static void AssertFailure(IngressSummaryCapture capture, string stage, string code)
    {
        Assert.Equal(stage, capture.FailureStage);
        Assert.Equal(code, capture.FailureCode);
    }

    private async Task<IngressSummaryCapture> RunAsync(string? csv, string? schema,
        BlobServiceClient? processorClient = null, string? csvChecksum = null, bool? included = null)
    {
        var windowId = Guid.NewGuid();
        var container = Client().GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();
        if (csv is not null)
            await container.GetBlobClient($"ingress/{CsvFile}").UploadAsync(BinaryData.FromString(csv), overwrite: true);
        if (schema is not null)
            await container.GetBlobClient($"schema/{SchemaFile}").UploadAsync(BinaryData.FromString(schema), overwrite: true);

        IReadOnlyList<IngressDataset> datasets = [Dataset(csv, schema, csvChecksum, included)];
        var capture = new IngressSummaryCapture(windowId, Guid.NewGuid(), datasets, DateTime.UtcNow);
        await DrainAsync(Processor(processorClient ?? Client()), windowId, datasets, capture);
        return capture;
    }

    private static IngressDataset Dataset(string? csv, string? schema, string? csvChecksum = null,
        bool? included = null) =>
        new("pupils", CsvFile, csvChecksum ?? Checksum(csv ?? string.Empty), SchemaFile,
            Checksum(schema ?? string.Empty), included);

    private static async Task DrainAsync(CsvSchemaFileProcessor processor, Guid windowId,
        IReadOnlyList<IngressDataset> datasets, IngressSummaryCapture capture)
    {
        await foreach (var _ in processor.ProcessAsync(windowId, CheckingExerciseType.PupilData, datasets,
                           summaryCapture: capture))
        {
        }
    }

    private static CsvSchemaFileProcessor Processor(BlobServiceClient client) => new(
        NullLogger<CsvSchemaFileProcessor>.Instance,
        new Dictionary<string, BlobServiceClient> { ["app"] = client }, new DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData.SampleDataGenerators([]));

    private BlobServiceClient Client() => new(fixture.ConnectionString);

    private BlobServiceClient FailingWhen(Func<Request, bool> fails)
    {
        var options = new BlobClientOptions();
        options.AddPolicy(new FailingPolicy(fails), HttpPipelinePosition.PerCall);
        return new BlobServiceClient(fixture.ConnectionString, options);
    }

    private sealed class FailingPolicy(Func<Request, bool> fails) : HttpPipelineSynchronousPolicy
    {
        public override void OnSendingRequest(HttpMessage message)
        {
            if (fails(message.Request))
                throw new RequestFailedException(500, "A failure that the test injects.");
        }
    }

    private static string Checksum(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
