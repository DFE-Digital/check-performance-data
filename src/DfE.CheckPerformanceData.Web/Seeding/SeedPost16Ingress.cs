using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Seeding;

public static class SeedPost16Ingress
{
    public static async Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath)
    {
        foreach (var windowId in new[] { DevDataSeeder.Post16IngressCheckingWindowId, DevDataSeeder.Post16FebruaryCheckingWindowId })
            await SeedWindowAsync(dbContext, blobs, ingress, contentRootPath, windowId);
    }

    private static async Task SeedWindowAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, ICheckingExerciseIngress ingress, string contentRootPath, Guid windowId)
    {
        var window = await dbContext.CheckingWindows
            .Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
            .SingleAsync(w => w.Id == windowId);
        var students = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.PupilData);
        var results = window.CheckingExercises.Single(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        var summaries = window.CheckingExercises.Where(e => e.DisplayOnly && e.TabName == "Summary").ToList();
        var container = blobs.GetBlobContainerClient(window.Id.ToString());
        await container.CreateIfNotExistsAsync();

        // Each dataset gets its ingress CSV and the schema that describes it. The schema is what
        // the page renders from, so each summary schema carries "layout": "vertical" and only the
        // columns its exercise's file has; a summary dataset is named after its schema.
        await LinkAsync(students, dataset => dataset.Name switch
        {
            "included" => "students-included_schema.json",
            "nonincluded" => "students-non-included_schema.json",
            _ => throw new InvalidOperationException($"Unexpected Post-16 student dataset: {dataset.Name}")
        });
        // A results slot is named by its source tag; every results file is the workbook's
        // included-results sheet, and the schema declares SOURCE so the slot's tag is stamped on
        // every record.
        await LinkAsync(results, dataset => dataset.Name switch
        {
            ResultsFileTags.Post16Main or ResultsFileTags.Post16LateResults1 => "results-included_schema.json",
            _ => throw new InvalidOperationException($"Unexpected Post-16 results dataset: {dataset.Name}")
        });
        foreach (var summary in summaries)
            await LinkAsync(summary, dataset => $"{dataset.Name}_schema.json");

        await dbContext.SaveChangesAsync();

        // Run each exercise's ingress so per-school output exists from first boot. The landing page
        // shows a window only when the school's pupil file exists under the exercise's own prefix,
        // and every dev seed recreates the exercises with new ids, so output from an earlier run is
        // orphaned — without this step the window vanished after every restart until an admin
        // clicked Validate. Same path the admin button drives, so the seed cannot drift from it.
        // Every summary exercise is ingressed, enabled or not, so enabling the next needs no more.
        foreach (var exercise in summaries.Append(students).Append(results))
        {
            ValidationProgress? last = null;
            await foreach (var progress in ingress.ProcessAsync(exercise.Id)) last = progress;
            if (last is not { IsComplete: true, IsError: false })
                throw new InvalidOperationException(
                    $"Post-16 ingress seed failed for exercise '{exercise.Name}': {last?.Message}");
        }

        // Mirror the first student dataset's scalar columns for rollback compatibility,
        // just as the admin upload actions do.
        var first = students.Datasets.OrderBy(d => d.SortOrder).First();
        await dbContext.CheckingWindows.Where(w => w.Id == window.Id).ExecuteUpdateAsync(update => update
            .SetProperty(w => w.IngressFile, first.IngressFile)
            .SetProperty(w => w.IngressFileChecksum, first.IngressFileChecksum)
            .SetProperty(w => w.SchemaFile, first.SchemaFile)
            .SetProperty(w => w.SchemaFileChecksum, first.SchemaFileChecksum));

        async Task LinkAsync(CheckingExercise exercise, Func<CheckingWindowDataset, string> schemaFor)
        {
            foreach (var dataset in exercise.Datasets.OrderBy(d => d.SortOrder))
            {
                var ingressFile = $"{dataset.Name}.csv";
                var schemaFile = schemaFor(dataset);
                dataset.IngressFileChecksum = await UploadAsync(exercise, ingressFile, "text/csv", dataset.Id);
                dataset.SchemaFileChecksum = await UploadAsync(exercise, schemaFile, "application/json", dataset.Id);
                dataset.IngressFile = CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, ingressFile);
                dataset.SchemaFile = CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, dataset.Id, schemaFile);
            }
        }

        async Task<string> UploadAsync(CheckingExercise exercise, string filename, string contentType, Guid definitionId)
        {
            await using var stream = File.OpenRead(Path.Combine(contentRootPath, "Data", "Ingress", "post16", filename));
            var checksum = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            stream.Position = 0;
            await container.GetBlobClient(CheckingExerciseBlobPaths.DefinitionFile(exercise.Id, definitionId, filename)).UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { ["sha256"] = checksum }
            });
            return checksum;
        }
    }
}
