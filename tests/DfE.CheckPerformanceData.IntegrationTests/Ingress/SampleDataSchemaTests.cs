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

// #939-#941: the rows of each generator pass validation against each schema that names it. The
// test reads every schema that ships in Data/Ingress, so a new schema or a changed one is checked
// with no change here. It pins no values.
[Collection(nameof(AzuriteCollection))]
public sealed class SampleDataSchemaTests(AzuriteFixture fixture)
{
    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    private static readonly string IngressFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Ingress");

    public static TheoryData<string> SchemasThatNameAGenerator()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(IngressFolder, "*.json", SearchOption.AllDirectories).Order())
        {
            if (JObject.Parse(File.ReadAllText(file))["x-ingress"]?["sample"] is not null)
                data.Add(Path.GetRelativePath(IngressFolder, file));
        }
        return data;
    }

    private static string Checksum(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    [Theory]
    [MemberData(nameof(SchemasThatNameAGenerator))]
    public async Task The_generated_rows_of_a_school_pass_the_schema(string schemaFile)
    {
        var schema = await File.ReadAllTextAsync(Path.Combine(IngressFolder, schemaFile));
        var json = JObject.Parse(schema);
        var institutionKey = json["x-ingress"]?["institutionKey"]?.Value<string>() ?? "LAESTAB";

        // A file with a header and no rows: the school is not in the file, so every row it gets is
        // generated.
        var csv = $"{institutionKey}\n";
        var windowId = Guid.NewGuid();
        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();
        foreach (var (path, content) in new[] { ("ingress/data.csv", csv), ("schema/data.json", schema) })
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await container.GetBlobClient(path).UploadAsync(stream, overwrite: true);
        }

        // A schema whose inclusion rule cannot decide needs its slot to say whether the file is included.
        var inclusionDecides = json["x-ingress"]?["inclusion"]?["included"] is not null;
        bool? included = json["properties"]?["INCLUDED"] is not null && !inclusionDecides ? true : null;

        var processor = new CsvSchemaFileProcessor(NullLogger<CsvSchemaFileProcessor>.Instance,
            new Dictionary<string, BlobServiceClient> { ["app"] = _blobs },
            new SampleDataGenerators(SampleDataGeneratorCatalogue.All));
        IReadOnlyList<IngressDataset> datasets =
            [new("data", "data.csv", Checksum(csv), "data.json", Checksum(schema), included, SourceFile: "TAG")];

        List<ValidationProgress> progress = [];
        await foreach (var p in processor.ProcessAsync(windowId, CheckingExerciseType.PupilData, datasets,
                           sampleSchools: [new SampleSchool("9334290", "123456")]))
            progress.Add(p);

        var last = progress[^1];
        Assert.False(last.IsError, last.Message);
        var output = container.GetBlobClient("data/9334290_pupils.json");
        Assert.True(await output.ExistsAsync());
        Assert.NotEmpty(JArray.Parse((await output.DownloadContentAsync()).Value.Content.ToString()));
    }
}
