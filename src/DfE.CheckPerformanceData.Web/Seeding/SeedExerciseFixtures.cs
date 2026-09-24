using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CsvHelper;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Seeds the E2E fixture windows (both KS4 June windows and both plain 16-19 windows) the way an
/// admin would: each dataset gets a CSV and a schema, and each exercise is run through ingress.
/// Every fixture window therefore has a release, and the Check Your Pupil Data page draws its tabs
/// from the schemas — a table with a CSV download, the same as the ingressed windows.
/// </summary>
/// <remarks>
/// The CSVs are generated from <see cref="SeedPupilData"/> and <see cref="SeedStudentResults"/>, so
/// the fixtures the E2E suite drives by name (Alice Smith, the Kingsmead duplicate pair, students
/// 500001-500003 and their results) are unchanged.
/// </remarks>
public static class SeedExerciseFixtures
{
    private static readonly Guid[] Ks4WindowIds =
        [DevDataSeeder.KeyStage4JuneCheckingWindowId, DevDataSeeder.ClosedKeyStage4JuneCheckingWindowId];

    // Both plain 16-19 windows hold the same students and the same results.
    private static readonly Guid[] Post16WindowIds = SeedStudentResults.WindowIds;

    public static Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath) =>
        ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, Ks4WindowIds, Post16WindowIds);

    /// <summary>Seeds the given windows, which must already exist with the exercises
    /// <see cref="SeedCheckingWindows"/> gives them. The ids are a parameter for the tests.</summary>
    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath,
        IReadOnlyList<Guid> ks4WindowIds, IReadOnlyList<Guid> post16WindowIds)
    {
        var schemaFolder = Path.Combine(contentRootPath, "Data", "Ingress", "schema");

        foreach (var windowId in ks4WindowIds)
        {
            var window = await LoadAsync(dbContext, windowId);
            var pupils = Exercise(window, CheckingExerciseType.PupilData);
            var schema = await File.ReadAllTextAsync(Path.Combine(schemaFolder, "ks4", "pupils.json"));
            foreach (var dataset in pupils.Datasets)
                await LinkAsync(blobs, pupils, dataset, "pupils.csv", RecordsCsv(SeedPupilData.Ks4Pupils(windowId)),
                    "pupils.json", schema);
            await dbContext.SaveChangesAsync();
            await IngestAsync(blobs, ingress, pupils);
        }

        foreach (var windowId in post16WindowIds)
        {
            var window = await LoadAsync(dbContext, windowId);
            var students = Exercise(window, CheckingExerciseType.PupilData);
            var all = SeedPupilData.Post16Pupils(windowId);
            foreach (var dataset in students.Datasets)
            {
                var schemaFile = dataset.Included == true ? "students-included.json" : "students-non-included.json";
                var schema = await File.ReadAllTextAsync(Path.Combine(schemaFolder, "post16", schemaFile));
                await LinkAsync(blobs, students, dataset, $"{dataset.Name}.csv",
                    RecordsCsv(all.Where(p => p.Included == dataset.Included)), schemaFile, schema);
            }

            var results = Exercise(window, CheckingExerciseType.ResultsEnquiry);
            var resultsSchema = WithResultKeys(
                await File.ReadAllTextAsync(Path.Combine(schemaFolder, "post16", "results-included.json")));
            // Every school is generated from the same ids, and the results are all Kingsmead's.
            var byCypmd = all.Where(p => p.Laestab == SeedStudentResults.Laestab.Replace("/", string.Empty))
                .ToDictionary(p => p.Cypmd_Id);
            foreach (var dataset in results.Datasets)
                await LinkAsync(blobs, results, dataset, $"{dataset.Name}.csv",
                    ResultsCsv(SeedStudentResults.All.Where(r => r.SourceFile == dataset.SourceFile), byCypmd),
                    "results-included.json", resultsSchema);

            await dbContext.SaveChangesAsync();
            await IngestAsync(blobs, ingress, students);
            await IngestAsync(blobs, ingress, results);
        }
    }

    private static Task<CheckingWindow> LoadAsync(IPortalDbContext dbContext, Guid windowId) =>
        dbContext.CheckingWindows
            .Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
            .SingleAsync(w => w.Id == windowId);

    private static CheckingExercise Exercise(CheckingWindow window, CheckingExerciseType type) =>
        window.CheckingExercises.Single(e => e.ExerciseType == type);

    // Same path the admin Validate button drives, so the seed cannot drift from it. A failure
    // quotes the head of the run's error log: the progress message is only a count.
    private static async Task IngestAsync(BlobServiceClient blobs, ICheckingExerciseIngress ingress, CheckingExercise exercise)
    {
        ValidationProgress? last = null;
        await foreach (var progress in ingress.ProcessAsync(exercise.Id)) last = progress;
        if (last is { IsComplete: true, IsError: false }) return;

        var log = blobs.GetBlobContainerClient(exercise.CheckingWindowId.ToString())
            .GetBlobClient($"{CheckingExerciseBlobPaths.LogPrefix(exercise.Id)}error_log.txt");
        var head = await log.ExistsAsync()
            ? string.Join(Environment.NewLine, (await log.DownloadContentAsync()).Value.Content.ToString()
                .Split('\n').Take(10))
            : string.Empty;
        throw new InvalidOperationException(
            $"Fixture ingress seed failed for exercise '{exercise.Name}' ({exercise.CheckingWindowId}): {last?.Message}{Environment.NewLine}{head}");
    }

    // The enquiry journey reads a result by QAN, qualification name, syllabus and session
    // (StudentResultRecord), which the supplier's results schema does not carry yet.
    private static string WithResultKeys(string schemaJson) => AddProperties(schemaJson,
        ("QAN", Shown("QAN", 20)),
        ("QUAL_NAME", Shown("Qualification", 21)),
        ("SYLLABUS", Shown("Syllabus", 22)),
        ("SESSION", Shown("Session", 23)));

    // No x-csv: an x-csv without column positions means "not exported", and these are.
    private static JsonObject Shown(string label, int order) => new()
    {
        ["type"] = new JsonArray("string", "null"),
        ["x-display"] = new JsonObject { ["label"] = label, ["visible"] = true, ["order"] = order }
    };

    private static string AddProperties(string schemaJson, params (string Name, JsonObject Definition)[] additions)
    {
        var root = JsonNode.Parse(schemaJson)!.AsObject();
        var properties = root["properties"]!.AsObject();
        foreach (var (name, definition) in additions)
            properties[name] = definition;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // Ours, not the supplier's: ingress generates a pupil's Id, and nothing reads the window id.
    private static readonly HashSet<string> NotSupplierColumns = ["Id", "CheckingWindowId"];

    // One row per record, one column per JSON property, serialised exactly as the read models
    // bind them. Columns the schema does not declare are dropped by ingress.
    private static byte[] RecordsCsv<T>(IEnumerable<T> records) =>
        WriteCsv(records.Select(record =>
            JsonSerializer.SerializeToNode(record, PupilDataBlobClient.JsonOptions)!.AsObject()
                .Where(p => !NotSupplierColumns.Contains(p.Key))
                .ToDictionary(p => p.Key, p => Text(p.Value))));

    // A result row carries the supplier's student columns as well as the result, so the Results
    // tab reads like the supplier's file rather than a list of bare codes.
    private static byte[] ResultsCsv(
        IEnumerable<StudentResultRecord> results, IReadOnlyDictionary<string, Post16PupilRecord> students) =>
        WriteCsv(results.Select(result =>
        {
            var student = students[result.CypmdId];
            return new Dictionary<string, string>
            {
                ["ULN"] = student.Uln,
                ["CYPMD_ID"] = result.CypmdId,
                ["SURNAME"] = student.Surname,
                ["FORENAMES"] = student.Firstname,
                ["SEX"] = student.Sex,
                ["DOB"] = student.DateOfBirth,
                ["AGE"] = student.Age.ToString(CultureInfo.InvariantCulture),
                ["EXAMYEAR"] = result.Session[1..],
                ["SEASON"] = result.Session[..1],
                ["Short_Qual_Desc"] = SupplierQualifications[result.Qan].Short,
                ["SubjectDescription"] = SupplierQualifications[result.Qan].Subject,
                ["GRADE"] = result.Grade,
                ["UKPRN"] = student.Ukprn,
                ["URN"] = student.Urn,
                ["LAESTAB"] = student.Laestab,
                ["QAN"] = result.Qan,
                ["QUAL_NAME"] = result.QualificationName,
                ["SYLLABUS"] = result.SyllabusCode,
                ["SESSION"] = result.Session
            };
        }));

    // The supplier's results file splits a qualification into a short type (at most 12 characters)
    // and a subject. One row per QAN in SeedStudentResults' catalogue.
    private static readonly IReadOnlyDictionary<string, (string Short, string Subject)> SupplierQualifications =
        new Dictionary<string, (string, string)>
        {
            ["60146084"] = ("GCSE", "Mathematics"),
            ["60148366"] = ("GCSE", "English Language"),
            ["60149589"] = ("GCE A", "Art and Design"),
            ["60172186"] = ("BTEC NEC", "Sport"),
            ["10025480"] = ("FSMQ", "Additional Maths"),
            ["50034157"] = ("IB", "International Baccalaureate")
        };

    private static byte[] WriteCsv(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        var list = rows.ToList();
        var headers = list.SelectMany(r => r.Keys).Distinct().ToList();
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var header in headers) csv.WriteField(header);
            csv.NextRecord();
            foreach (var row in list)
            {
                foreach (var header in headers) csv.WriteField(row.GetValueOrDefault(header, string.Empty));
                csv.NextRecord();
            }
        }
        return Encoding.UTF8.GetBytes(writer.ToString());
    }

    private static string Text(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue(out string? text) => text ?? string.Empty,
        _ => node.ToJsonString()
    };

    private static async Task LinkAsync(BlobServiceClient blobs, CheckingExercise exercise, CheckingWindowDataset dataset,
        string ingressFile, byte[] csv, string schemaFile, string schema)
    {
        var container = blobs.GetBlobContainerClient(exercise.CheckingWindowId.ToString());
        await container.CreateIfNotExistsAsync();
        dataset.IngressFileChecksum = await UploadAsync(container, exercise, dataset, ingressFile, csv, "text/csv");
        dataset.SchemaFileChecksum = await UploadAsync(container, exercise, dataset, schemaFile,
            Encoding.UTF8.GetBytes(schema), "application/json");
        dataset.IngressFile = CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, ingressFile);
        dataset.SchemaFile = CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, schemaFile);
    }

    private static async Task<string> UploadAsync(BlobContainerClient container, CheckingExercise exercise,
        CheckingWindowDataset dataset, string fileName, byte[] content, string contentType)
    {
        var checksum = Convert.ToHexString(SHA256.HashData(content));
        await container.GetBlobClient(CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, fileName))
            .UploadAsync(new BinaryData(content), new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { ["sha256"] = checksum }
            });
        return checksum;
    }
}
