using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Uploads the image-bundled AODC grade reference document to the rules-config container when the
/// blob is absent. AB#296648 / AB#297130.
///
/// Seed-if-missing, never overwrite — matching how <c>country-languages.json</c> is treated, and for
/// the same reason: once the real AODC export has been loaded into an environment, a redeploy of an
/// older bundled copy must not undo it.
/// </summary>
public static class SeedGradeReference
{
    public static async Task ExecuteSeedAsync(
        GradeReferenceBlobClient client, string contentRootPath, CancellationToken ct = default)
    {
        var path = Path.Combine(contentRootPath, "Data", "GradeReference", "grade-reference.json");
        if (!File.Exists(path)) return;

        await client.SeedIfMissingAsync(await File.ReadAllTextAsync(path, ct), ct);
    }
}
