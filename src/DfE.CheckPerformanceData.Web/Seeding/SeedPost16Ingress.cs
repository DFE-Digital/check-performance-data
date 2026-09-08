using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Seeding;

public static class SeedPost16Ingress
{
    public static async Task ExecuteSeedAsync(
        IPortalDbContext dbContext, BlobServiceClient blobs, string contentRootPath)
    {
        var window = await dbContext.CheckingWindows
            .Include(w => w.CheckingExercises).ThenInclude(e => e.Datasets)
            .SingleAsync(w => w.Id == DevDataSeeder.Post16IngressCheckingWindowId);
        var datasets = window.CheckingExercises
            .Single(e => e.ExerciseType == CheckingExerciseType.PupilData).Datasets;
        var container = blobs.GetBlobContainerClient(window.Id.ToString());
        await container.CreateIfNotExistsAsync();

        foreach (var dataset in datasets.OrderBy(d => d.SortOrder))
        {
            var ingressFile = $"{dataset.Name}.csv";
            var schemaFile = $"{dataset.Name}.json";
            var ingressChecksum = await UploadAsync("ingress", ingressFile, "text/csv");
            var schemaChecksum = await UploadAsync("schema", schemaFile, "application/json");
            dataset.IngressFile = ingressFile;
            dataset.IngressFileChecksum = ingressChecksum;
            dataset.SchemaFile = schemaFile;
            dataset.SchemaFileChecksum = schemaChecksum;

        }

        await dbContext.SaveChangesAsync();

        // Mirror the first dataset's scalar columns for rollback compatibility,
        // just as the admin upload actions do.
        var first = datasets.OrderBy(d => d.SortOrder).First();
        await dbContext.CheckingWindows.Where(w => w.Id == window.Id).ExecuteUpdateAsync(update => update
            .SetProperty(w => w.IngressFile, first.IngressFile)
            .SetProperty(w => w.IngressFileChecksum, first.IngressFileChecksum)
            .SetProperty(w => w.SchemaFile, first.SchemaFile)
            .SetProperty(w => w.SchemaFileChecksum, first.SchemaFileChecksum));

        async Task<string> UploadAsync(string folder, string filename, string contentType)
        {
            await using var stream = File.OpenRead(Path.Combine(contentRootPath, "Data", "Ingress", folder, filename));
            var checksum = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            stream.Position = 0;
            await container.GetBlobClient($"{folder}/{filename}").UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { ["sha256"] = checksum }
            });
            return checksum;
        }
    }
}
