using DfE.CheckPerformanceData.Application.WindowManagement.Extensions;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

// #938: ingress replaces the rows of the sample schools with generated rows, and fails closed
// when it cannot. A test generator stands in for the real ones.
[Collection(nameof(AzuriteCollection))]
public sealed class SampleDataIngressTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private const string RealSchool = "8604070";
    private const string SampleLaestab = "9334290";
    private const string AbsentLaestab = "9334291";

    private static string Schema(string? sample) => $$"""
    {
      "type": "object",
      {{(sample is null ? "" : $"\"x-ingress\": {{ \"sample\": \"{sample}\" }},")}}
      "properties": {
        "Id":       { "type": ["string", "null"] },
        "SURNAME":  { "type": ["string", "null"] },
        "LAESTAB":  { "type": ["string", "null"] },
        "URN":      { "type": ["string", "null"] },
        "AGE":      { "type": ["integer", "null"] }
      }
    }
    """;

    // The sample school is in the file with its slash, as some supplier files write it.
    private const string Csv =
        "SURNAME,LAESTAB,URN,AGE\nReal,8604070,100001,10\nRealToo,933/4290,123456,11\n";

    private sealed class TestGenerator(string name, string age = "10") : ISampleDataGenerator
    {
        public string Name => name;

        public IEnumerable<IReadOnlyDictionary<string, string>> Rows(SampleSchool school, SampleSchemaShape schema) =>
        [
            new Dictionary<string, string> { ["SURNAME"] = "Sample", ["URN"] = school.Urn, ["AGE"] = age },
            new Dictionary<string, string> { ["SURNAME"] = "Sample", ["URN"] = school.Urn, ["AGE"] = age }
        ];
    }

    private static readonly IReadOnlyList<SampleSchool> SampleSchools =
        [new SampleSchool(SampleLaestab, "123456"), new SampleSchool(AbsentLaestab, "654321")];

    private static string Checksum(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private async Task<(Guid WindowId, BlobContainerClient Container)> SeedAsync(string schema)
    {
        var windowId = Guid.NewGuid();
        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();
        foreach (var (path, content) in new[] { ("ingress/pupils.csv", Csv), ("schema/pupils.json", schema) })
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await container.GetBlobClient(path).UploadAsync(stream, overwrite: true);
        }
        return (windowId, container);
    }

    private async Task<List<ValidationProgress>> RunAsync(Guid windowId, string schema, params ISampleDataGenerator[] generators)
    {
        var processor = new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
            new Dictionary<string, BlobServiceClient> { ["app"] = _blobs }, new SampleDataGenerators(generators));
        IReadOnlyList<IngressDataset> datasets =
            [new("pupils", "pupils.csv", Checksum(Csv), "pupils.json", Checksum(schema), Included: null)];

        List<ValidationProgress> all = [];
        await foreach (var p in processor.ProcessAsync(windowId, CheckingExerciseType.PupilData, datasets,
                           sampleSchools: SampleSchools))
            all.Add(p);
        return all;
    }

    private static async Task<JArray?> ReadAsync(BlobContainerClient container, string laestab)
    {
        var blob = container.GetBlobClient($"data/{laestab}_pupils.json");
        if (!await blob.ExistsAsync()) return null;
        return JArray.Parse((await blob.DownloadContentAsync()).Value.Content.ToString());
    }

    [Fact]
    public async Task A_sample_school_gets_only_generated_rows_and_other_schools_keep_their_real_rows()
    {
        var schema = Schema("test-pupils");
        var (windowId, container) = await SeedAsync(schema);

        var last = (await RunAsync(windowId, schema, new TestGenerator("test-pupils")))[^1];

        Assert.False(last.IsError, last.Message);
        var sample = await ReadAsync(container, SampleLaestab);
        Assert.NotNull(sample);
        Assert.Equal(2, sample.Count);
        Assert.All(sample, r => Assert.Equal("Sample", r["SURNAME"]!.Value<string>()));
        // The generated row went through the type conversion like a supplier row.
        Assert.All(sample, r => Assert.Equal(JTokenType.Integer, r["AGE"]!.Type));

        var real = Assert.Single((await ReadAsync(container, RealSchool))!);
        Assert.Equal("Real", real["SURNAME"]!.Value<string>());
    }

    [Fact]
    public async Task The_run_names_the_sample_schools_and_its_summary_marks_them()
    {
        var schema = Schema("test-pupils");
        var (windowId, _) = await SeedAsync(schema);

        var progress = await RunAsync(windowId, schema, new TestGenerator("test-pupils"));

        var step = Assert.Single(progress, p => p.Phase == "Sample data");
        Assert.Contains(LaestabList.Display(SampleLaestab), step.Message);
        Assert.Contains(LaestabList.Display(AbsentLaestab), step.Message);

        var summary = progress[^1].SchoolSummary!;
        Assert.Equal(
            [SampleLaestab, AbsentLaestab],
            summary.Where(s => s.UsesSampleData).Select(s => s.Laestab).Order());
        Assert.False(summary.Single(s => s.Laestab == RealSchool).UsesSampleData);
    }

    [Fact]
    public async Task A_sample_school_that_is_not_in_the_file_gets_generated_rows()
    {
        var schema = Schema("test-pupils");
        var (windowId, container) = await SeedAsync(schema);

        await RunAsync(windowId, schema, new TestGenerator("test-pupils"));

        var absent = await ReadAsync(container, AbsentLaestab);
        Assert.NotNull(absent);
        Assert.All(absent, r => Assert.Equal("654321", r["URN"]!.Value<string>()));
    }

    [Fact]
    public async Task A_schema_that_names_no_generator_fails_the_run_and_writes_nothing()
    {
        var schema = Schema(sample: null);
        var (windowId, container) = await SeedAsync(schema);

        var last = (await RunAsync(windowId, schema, new TestGenerator("test-pupils")))[^1];

        Assert.True(last.IsError);
        Assert.Contains("names no sample data generator", last.Message);
        Assert.Null(await ReadAsync(container, RealSchool));
        Assert.Null(await ReadAsync(container, SampleLaestab));
    }

    [Fact]
    public async Task A_schema_that_names_an_unknown_generator_fails_the_run_by_name()
    {
        var schema = Schema("no-such-generator");
        var (windowId, container) = await SeedAsync(schema);

        var last = (await RunAsync(windowId, schema, new TestGenerator("test-pupils")))[^1];

        Assert.True(last.IsError);
        Assert.Contains("'no-such-generator'", last.Message);
        Assert.Null(await ReadAsync(container, SampleLaestab));
    }

    [Fact]
    public async Task A_generated_row_that_fails_the_schema_fails_the_run()
    {
        var schema = Schema("bad-pupils");
        var (windowId, container) = await SeedAsync(schema);

        var last = (await RunAsync(windowId, schema, new TestGenerator("bad-pupils", age: "ten")))[^1];

        Assert.True(last.IsError);
        Assert.Null(await ReadAsync(container, SampleLaestab));
    }
}
