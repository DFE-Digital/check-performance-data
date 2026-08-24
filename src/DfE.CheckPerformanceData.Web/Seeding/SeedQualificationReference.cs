using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Uploads the image-bundled QualList qualification reference document to the rules-config
/// container when the blob is absent. AB#297848.
///
/// Seed-if-missing, never overwrite — matching how <c>grade-reference.json</c> is treated, and for
/// the same reason: once the real QualList export has been loaded into an environment, a redeploy
/// of an older bundled copy must not undo it.
/// </summary>
public static class SeedQualificationReference
{
    public static async Task ExecuteSeedAsync(
        QualificationReferenceBlobClient client, string contentRootPath, CancellationToken ct = default)
    {
        var path = Path.Combine(contentRootPath, "Data", "QualificationReference", "qualification-reference.json");
        if (!File.Exists(path)) return;

        await client.SeedIfMissingAsync(await File.ReadAllTextAsync(path, ct), ct);
    }
}
