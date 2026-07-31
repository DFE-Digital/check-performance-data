using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

/// <summary>
/// The whole 16-19 pupils slice through its real components: two supplier CSVs -> ingress ->
/// one merged blob per school -> PupilDataBlobClient (Post16 record shape) -> repository ->
/// service -> the Post16 column set. No substitutes except the signed-in user.
///
/// This is the automated stand-in for clicking through the app, which needs DfE Sign-in
/// credentials that CI does not have.
/// </summary>
[Collection(nameof(AzuriteCollection))]
public sealed class Post16EndToEndTests(AzuriteFixture azurite, PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    private const string Laestab = "860/4070";
    private const string LaestabBlobKey = "8604070";

    private readonly BlobServiceClient _blobs = new(azurite.ConnectionString);

    private const string IncludedSchema = """
    {
      "type": "object",
      "properties": {
        "Id":        { "type": ["string", "null"] },
        "INCLUDED":  { "type": "boolean" },
        "CYPMD_ID":  { "type": ["string", "null"] },
        "SURNAME":   { "type": ["string", "null"] },
        "FORENAMES": { "type": ["string", "null"] },
        "SEX":       { "type": ["string", "null"] },
        "DOB":       { "type": ["string", "null"] },
        "AGE":       { "type": ["integer", "null"] },
        "LAESTAB":   { "type": ["string", "null"] },
        "URN":       { "type": ["string", "null"] },
        "UKPRN":     { "type": ["string", "null"] },
        "ULN":       { "type": ["string", "null"] },
        "P_INCL":    { "type": ["integer", "null"] }
      }
    }
    """;

    private const string NonIncludedSchema = """
    {
      "type": "object",
      "properties": {
        "Id":        { "type": ["string", "null"] },
        "INCLUDED":  { "type": "boolean" },
        "CYPMD_ID":  { "type": ["string", "null"] },
        "SURNAME":   { "type": ["string", "null"] },
        "FORENAMES": { "type": ["string", "null"] },
        "SEX":       { "type": ["string", "null"] },
        "DOB":       { "type": ["string", "null"] },
        "AGE":       { "type": ["integer", "null"] },
        "LAESTAB":   { "type": ["string", "null"] },
        "URN":       { "type": ["string", "null"] },
        "UKPRN":     { "type": ["string", "null"] },
        "ULN":       { "type": ["string", "null"] },
        "CampID_0":  { "type": ["string", "null"] }
      }
    }
    """;

    private const string IncludedCsv =
        "CYPMD_ID,SURNAME,FORENAMES,SEX,DOB,AGE,LAESTAB,URN,UKPRN,ULN,P_INCL\n" +
        "500001,Adams,Alice,F,2007-09-01 00:00:00.0000000,18,8604070,136309,10001234,9900000001,501\n";

    private const string NonIncludedCsv =
        "CYPMD_ID,SURNAME,FORENAMES,SEX,DOB,AGE,LAESTAB,URN,UKPRN,ULN,CampID_0\n" +
        "500002,Baker,Bob,M,2007-01-15 00:00:00.0000000,18,8604070,136309,10001234,9900000002,C0\n";

    private static string Checksum(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static IReadOnlyList<IngressDataset> Datasets() =>
    [
        new("included", "included.csv", Checksum(IncludedCsv), "included.json", Checksum(IncludedSchema), Included: true),
        new("nonincluded", "nonincluded.csv", Checksum(NonIncludedCsv), "nonincluded.json", Checksum(NonIncludedSchema), Included: false)
    ];

    private async Task<Guid> SeedPost16WindowAsync()
    {
        var windowId = Guid.NewGuid();

        await using (var ctx = postgres.CreateContext())
        {
            ctx.CheckingWindows.Add(new CheckingWindow
            {
                Id = windowId,
                StartDate = DateTime.Now.AddDays(-1),
                EndDate = DateTime.Now.AddDays(13),
                KeyStage = KeyStages.Post16,
                CheckingWindowType = CheckingWindowType.Post16,
                Title = "16 to 19"
            });
            await ctx.SaveChangesAsync();
        }

        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        foreach (var (path, content) in new[]
                 {
                     ("ingress/included.csv", IncludedCsv),
                     ("ingress/nonincluded.csv", NonIncludedCsv),
                     ("schema/included.json", IncludedSchema),
                     ("schema/nonincluded.json", NonIncludedSchema)
                 })
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await container.GetBlobClient(path).UploadAsync(stream, overwrite: true);
        }

        return windowId;
    }

    private async Task RunIngressAsync(Guid windowId, bool clearExistingFiles = false)
    {
        var processor = new CsvSchemaFileProcessor(
            NullLogger<CsvSchemaFileProcessor>.Instance,
            new Dictionary<string, BlobServiceClient> { ["app"] = _blobs });

        ValidationProgress? last = null;
        await foreach (var p in processor.ProcessAsync(windowId, Datasets(), clearExistingFiles: clearExistingFiles))
        {
            last = p;
        }

        Assert.NotNull(last);
        Assert.False(last!.IsError, last.Message);
    }

    private CheckYourPupilDataService BuildService()
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.OrganisationLaestab.Returns(Laestab);
        currentUser.OrganisationUrn.Returns("136309");

        var repository = new CheckYourPupilDataRepository(
            postgres.CreateContext(),
            new PupilDataBlobClient(_blobs),
            new MemoryCache(new MemoryCacheOptions()));

        return new CheckYourPupilDataService(repository, currentUser);
    }

    [Fact]
    public async Task Two_supplier_csvs_reach_the_page_as_two_populations_in_one_pupils_tab()
    {
        var windowId = await SeedPost16WindowAsync();
        await RunIngressAsync(windowId);

        var service = BuildService();

        var (includedTable, includedTotal) = await service.GetPupilTableAsync(windowId, included: true, null, 0, 10);
        var (nonIncludedTable, nonIncludedTotal) = await service.GetPupilTableAsync(windowId, included: false, null, 0, 10);

        // Each supplier file becomes exactly one population — inclusion by file of origin.
        Assert.Equal(1, includedTotal);
        Assert.Equal(1, nonIncludedTotal);
        Assert.Equal("Adams", includedTable.Rows[0][0]);
        Assert.Equal("Baker", nonIncludedTable.Rows[0][0]);

        // The included table carries the inclusion-status column; the non-included one cannot,
        // because that supplier file has no P_INCL at all.
        Assert.Contains("Inclusion status", includedTable.Headers);
        Assert.DoesNotContain("Inclusion status", nonIncludedTable.Headers);

        // ULN, not UPN — and the supplier's timestamp DOB is displayed as dd/MM/yyyy.
        Assert.Contains("ULN", includedTable.Headers);
        Assert.Equal("9900000001", includedTable.Rows[0][6]);
        Assert.Equal("01/09/2007", includedTable.Rows[0][3]);
    }

    [Fact]
    public async Task The_csv_export_uses_the_post16_column_set()
    {
        var windowId = await SeedPost16WindowAsync();
        await RunIngressAsync(windowId);

        var service = BuildService();

        var included = await service.GetPupilCsvAsync(windowId, included: true);
        var nonIncluded = await service.GetPupilCsvAsync(windowId, included: false);

        Assert.Equal("ULN", included.Headers[0]);
        Assert.DoesNotContain("UPN", included.Headers);
        Assert.Contains("UKPRN", included.Headers);

        // The non-included export carries the fields unique to that file instead of inclusion flags.
        Assert.Contains("CampID 0", nonIncluded.Headers);
        Assert.DoesNotContain("Pupil Inclusion Status Flag", nonIncluded.Headers);
        Assert.Equal("C0", nonIncluded.Rows[0][^2]);
    }

    // The specific failure the single-run merge exists to prevent: "clear existing files" wipes
    // the whole data/ prefix, so ingesting the two 16-19 files as two runs would leave only the
    // second population. One run must survive it with both intact.
    [Fact]
    public async Task Re_running_ingress_with_clear_existing_files_keeps_both_populations()
    {
        var windowId = await SeedPost16WindowAsync();
        await RunIngressAsync(windowId);
        await RunIngressAsync(windowId, clearExistingFiles: true);

        var service = BuildService();

        var (_, includedTotal) = await service.GetPupilTableAsync(windowId, included: true, null, 0, 10);
        var (_, nonIncludedTotal) = await service.GetPupilTableAsync(windowId, included: false, null, 0, 10);

        Assert.Equal(1, includedTotal);
        Assert.Equal(1, nonIncludedTotal);
    }

    [Fact]
    public async Task Both_populations_live_in_a_single_blob_per_school()
    {
        var windowId = await SeedPost16WindowAsync();
        await RunIngressAsync(windowId);

        // The blob layout is unchanged from KS4 — one file per school — which is the whole point
        // of merging at ingest rather than fanning out the read path.
        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        var blob = container.GetBlobClient($"data/{LaestabBlobKey}_pupils.json");
        Assert.True(await blob.ExistsAsync());

        var pupils = await new PupilDataBlobClient(_blobs)
            .GetPupilsAsync(windowId, Laestab, CheckingWindowType.Post16);

        Assert.NotNull(pupils);
        Assert.Equal(2, pupils!.Count);
        Assert.All(pupils, p => Assert.IsType<Post16PupilRecord>(p));
        Assert.Single(pupils, p => p.IsIncluded);
        Assert.Single(pupils, p => !p.IsIncluded);
    }
}
