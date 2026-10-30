using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Loads the establishment directory from the app storage files container on startup. The bundled
/// copy is embedded so it remains available even when publish output omits the source file.
/// </summary>
public sealed class EstablishmentDirectorySeedingService(
    BlobServiceClient blobServiceClient,
    EstablishmentDirectory directory,
    ILogger<EstablishmentDirectorySeedingService> logger) : IHostedService
{
    private const string ContainerName = "files";
    private const string BlobName = "Impersonation/EstablishmentDetails.csv";
    private const string ResourceName = "DfE.CheckPerformanceData.Web.Data.Impersonation.EstablishmentDetails.csv";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(EstablishmentDirectorySeedingService).Assembly;
        await using var resource = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Bundled establishment directory resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(resource);
        var bundledCsv = await reader.ReadToEndAsync(cancellationToken);

        // Keep the directory available if the storage account is temporarily unreachable.
        directory.LoadCsv(bundledCsv);

        try
        {
            var container = blobServiceClient.GetBlobContainerClient(ContainerName);
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var blob = container.GetBlobClient(BlobName);

            try
            {
                await blob.UploadAsync(
                    BinaryData.FromString(bundledCsv),
                    new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                    },
                    cancellationToken);
                logger.LogInformation("Seeded establishment directory blob {BlobName} in {ContainerName}.", BlobName, ContainerName);
            }
            catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
            {
                logger.LogInformation("Establishment directory blob {BlobName} already exists in {ContainerName}.", BlobName, ContainerName);
            }

            var storedCsv = await blob.DownloadContentAsync(cancellationToken);
            directory.LoadCsv(storedCsv.Value.Content.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not load the establishment directory from app storage; using the bundled copy.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
