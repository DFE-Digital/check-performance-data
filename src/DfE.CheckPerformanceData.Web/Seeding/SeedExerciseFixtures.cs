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
/// Seeds the E2E fixture windows (both KS4 June windows) the way an admin would: each dataset gets a CSV and a schema, and each exercise is run through ingress.
/// Every fixture window therefore has a release, and the Check Your Pupil Data page draws its tabs
/// from the schemas — a table with a CSV download, the same as the ingressed windows.
/// </summary>
/// <remarks>
/// The CSVs are generated from <see cref="SeedPupilData"/>, so the fixtures the E2E suite drives by
/// name (Alice Smith, the Kingsmead duplicate pair) are unchanged. The 16-19 window is not a
/// fixture: it is left for an admin to import (<see cref="SeedPost16OctoberSamples"/>).
/// </remarks>
public static class SeedExerciseFixtures
{
    private static readonly Guid[] Ks4WindowIds =
        [DevDataSeeder.KeyStage4JuneCheckingWindowId, DevDataSeeder.ClosedKeyStage4JuneCheckingWindowId];

    public static Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath) =>
        ExecuteSeedAsync(dbContext, blobs, ingress, contentRootPath, Ks4WindowIds);

    /// <summary>Seeds the given windows, which must already exist with the exercises
    /// <see cref="SeedCheckingWindows"/> gives them. The ids are a parameter for the tests.</summary>
    public static async Task ExecuteSeedAsync(IPortalDbContext dbContext, BlobServiceClient blobs,
        ICheckingExerciseIngress ingress, string contentRootPath,
        IReadOnlyList<Guid> ks4WindowIds)
    {
        var ingressFolder = Path.Combine(contentRootPath, "Data", "Ingress");

        foreach (var windowId in ks4WindowIds)
        {
            var window = await LoadAsync(dbContext, windowId);
            var pupils = Exercise(window, CheckingExerciseType.PupilData);
            var schema = await File.ReadAllTextAsync(Path.Combine(ingressFolder, "ks4june", "pupils_schema.json"));
            foreach (var dataset in pupils.Datasets)
                await LinkAsync(blobs, pupils, dataset, "pupils.csv", RecordsCsv(SeedPupilData.Ks4Pupils(windowId)),
                    "pupils_schema.json", schema);
            await dbContext.SaveChangesAsync();
            await IngestAsync(blobs, ingress, pupils);
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

    // Ours, not the supplier's: ingress generates a pupil's Id, and nothing reads the window id.
    private static readonly HashSet<string> NotSupplierColumns = ["Id", "CheckingWindowId"];

    // One row per record, one column per JSON property, serialised exactly as the read models
    // bind them. Columns the schema does not declare are dropped by ingress.
    internal static byte[] RecordsCsv<T>(IEnumerable<T> records) =>
        WriteCsv(records.Select(record =>
            JsonSerializer.SerializeToNode(record, PupilDataBlobClient.JsonOptions)!.AsObject()
                .Where(p => !NotSupplierColumns.Contains(p.Key))
                .ToDictionary(p => p.Key, p => Text(p.Value))));

    internal static byte[] WriteCsv(IEnumerable<IReadOnlyDictionary<string, string>> rows)
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
