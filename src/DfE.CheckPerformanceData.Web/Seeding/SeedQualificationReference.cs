using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Web.Seeding;

/// <summary>
/// Uploads each image-bundled qualification reference document to the rules-config
/// container when its blob is absent. AB#297848.
///
/// Seed-if-missing, never overwrite: once the real QualList export has been loaded into an
/// environment, a redeploy of an older bundled copy must not undo it.
/// </summary>
public static class SeedQualificationReference
{
    public static async Task ExecuteSeedAsync(
        QualificationReferenceBlobClient client, string contentRootPath, CancellationToken ct = default)
    {
        foreach (var set in Enum.GetValues<QualificationReferenceSet>())
        {
            var path = Path.Combine(contentRootPath, "Data", "QualificationReference",
                ResultsEnquiryBlobPaths.QualificationReferenceBlobNameFor(set));
            if (!File.Exists(path)) continue;

            await client.SeedIfMissingAsync(set, await File.ReadAllTextAsync(path, ct), ct);
        }
    }
}
