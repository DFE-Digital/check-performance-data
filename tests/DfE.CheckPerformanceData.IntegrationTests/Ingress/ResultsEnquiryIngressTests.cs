using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

/// <summary>
/// The results-enquiry slice of ingress (#324): several supplier CSVs -> one merged blob per
/// school, each row stamped with the tag of the file it came from -> StudentResultsBlobClient, the
/// client the enquiry journey itself reads through. Until this existed nothing but the dev-only
/// SeedStudentResults wrote that blob, so the journey had nothing to show on a deployed
/// environment.
/// </summary>
[Collection(nameof(AzuriteCollection))]
public sealed class ResultsEnquiryIngressTests(AzuriteFixture fixture)
{
    private const string Laestab = "860/4070";

    private readonly BlobServiceClient _blobs = new(fixture.ConnectionString);

    // The output contract (AB#296999). SOURCE is declared but never supplied by the CSV — the run
    // stamps it from the dataset slot, exactly as INCLUDED is stamped on 16-19 pupils.
    private const string Schema = """
    {
      "type": "object",
      "properties": {
        "CYPMD_ID":  { "type": ["string", "null"] },
        "QAN":       { "type": ["string", "null"] },
        "QUAL_NAME": { "type": ["string", "null"] },
        "SYLLABUS":  { "type": ["string", "null"] },
        "SESSION":   { "type": ["string", "null"] },
        "GRADE":     { "type": ["string", "null"] },
        "SOURCE":    { "type": "string" }
      }
    }
    """;

    private const string Header = "CYPMD_ID,QAN,QUAL_NAME,SYLLABUS,SESSION,GRADE,LAESTAB\n";

    private const string MainCsv = Header +
        "500001,6037116X,GCSE (9-1) Bus. Studs:Single,1BS0,S2024,5,8604070\n" +
        "500002,60181576,GCSE (9-1) French,1FR0,S2024,3,8604070\n";

    private const string LateResultsCsv = Header +
        "500001,60181576,GCSE (9-1) French,1FR0,S2024,6,8604070\n";

    private static string Checksum(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static IReadOnlyList<IngressDataset> Datasets() =>
    [
        new(ResultsFileTags.Post16Main, "main.csv", Checksum(MainCsv), "results.json", Checksum(Schema),
            Included: null, SourceFile: ResultsFileTags.Post16Main),
        new(ResultsFileTags.Post16LateResults1, "lr1.csv", Checksum(LateResultsCsv), "results.json", Checksum(Schema),
            Included: null, SourceFile: ResultsFileTags.Post16LateResults1)
    ];

    private async Task<Guid> SeedWindowAsync(params (string Path, string Content)[] files)
    {
        var windowId = Guid.NewGuid();
        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        await container.CreateIfNotExistsAsync();

        foreach (var (path, content) in files.Length > 0
                     ? files
                     : [("ingress/main.csv", MainCsv), ("ingress/lr1.csv", LateResultsCsv), ("schema/results.json", Schema)])
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await container.GetBlobClient(path).UploadAsync(stream, overwrite: true);
        }

        return windowId;
    }

    private async Task<ValidationProgress> RunAsync(Guid windowId, IReadOnlyList<IngressDataset>? datasets = null)
    {
        var processor = new CsvSchemaFileProcessor(
            NullLogger<CsvSchemaFileProcessor>.Instance,
            new Dictionary<string, BlobServiceClient> { ["app"] = _blobs });

        ValidationProgress? last = null;
        await foreach (var progress in processor.ProcessAsync(
                           windowId, CheckingExerciseType.ResultsEnquiry, datasets ?? Datasets(),
                           clearExistingFiles: true))
        {
            last = progress;
        }

        Assert.NotNull(last);
        return last!;
    }

    private IStudentResultsClient ResultsClient() =>
        new StudentResultsBlobClient(_blobs, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task A_clean_run_writes_one_merged_file_the_enquiry_journey_can_read()
    {
        var windowId = await SeedWindowAsync();

        var last = await RunAsync(windowId);
        Assert.False(last.IsError, last.Message);

        // Read back through the journey's own client, so the blob name, the JSON shape and the
        // property names are all checked by the code that has to consume them.
        var results = await ResultsClient().GetResultsAsync(windowId, Laestab, "500001");

        Assert.Equal(2, results.Count);
        var main = Assert.Single(results, r => r.SourceFile == ResultsFileTags.Post16Main);
        Assert.Equal("6037116X", main.Qan);
        Assert.Equal("GCSE (9-1) Bus. Studs:Single", main.QualificationName);
        Assert.Equal("1BS0", main.SyllabusCode);
        Assert.Equal("S2024", main.Session);
        Assert.Equal("5", main.Grade);
    }

    // A late results file as the supplier sends it (data specification, 16-19 and KS4 names). The
    // journey's QAN, QUAL_NAME, SYLLABUS and SESSION come from the schema's x-ingress.source
    // columns: GNUMBER, the description column, BRDSUBNO and the season column.
    [Theory]
    [InlineData("post16",
        "LAESTAB,CYPMD_ID,SURNAME,FORENAMES,AB_CODE_NDAQ,Short_Qual_Desc,EXAM_YEAR_SEASON,EXAM_DATE,Discount_Code,SYLLABUS_TITLE,GNUMBER,BRDSUBNO,GRADE,Late_Result_Type",
        "8604070,500001,Smith,Alice,AQA,GCSE,S2024,20240615,,GCSE (9-1) Mathematics,60146084,8300H,6,Amendment")]
    [InlineData("ks4autumn",
        "LAESTAB,CYPMD_ID,SURNAME,FORENAME,AB_CODE_NDAQ,QUALIFICATION_TYPE,SEASON_AND_YEAR,EXAM_DATE,WOLF_DISC_CODE,QUALIFICATION_DESCRIPTION,GNUMBER,BRDSUBNO,GRADE,Late_Result_Type",
        "8604070,500001,Smith,Alice,AQA,GCSE,S2024,20240615,,GCSE (9-1) Mathematics,60146084,8300H,6,Amendment")]
    public async Task A_supplier_late_results_file_reaches_the_journey_through_its_schemas_source_columns(
        string folder, string header, string row)
    {
        var schema = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", folder, "results-late_schema.json"));
        var csv = header + "\n" + row + "\n";
        var windowId = await SeedWindowAsync(("ingress/lr1.csv", csv), ("schema/late.json", schema));

        var last = await RunAsync(windowId,
        [
            new(ResultsFileTags.Post16LateResults1, "lr1.csv", Checksum(csv), "late.json", Checksum(schema),
                Included: null, SourceFile: ResultsFileTags.Post16LateResults1)
        ]);
        Assert.False(last.IsError, last.Message);

        var result = Assert.Single(await ResultsClient().GetResultsAsync(windowId, Laestab, "500001"));
        Assert.Equal("60146084", result.Qan);
        Assert.Equal("GCSE (9-1) Mathematics", result.QualificationName);
        Assert.Equal("8300H", result.SyllabusCode);
        Assert.Equal("S2024", result.Session);
        Assert.Equal("6", result.Grade);
        Assert.Equal(ResultsFileTags.Post16LateResults1, result.SourceFile);
    }

    // The 16-18 results data file as the supplier sends it (field references as headers). The
    // session is two columns, SEASON and EXAMYEAR, and the qualification is a type and a subject,
    // so the schema joins columns as well as renaming them.
    [Theory]
    [InlineData("results-included_schema.json")]
    [InlineData("results-non-included_schema.json")]
    public async Task A_supplier_results_file_reaches_the_journey_with_joined_session_and_qualification(string schemaFile)
    {
        var schema = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Data", "Ingress", "post16", schemaFile));
        const string csv =
            "ULN,CYPMD_ID,SURNAME,FORENAMES,SEX,DOB,AGE,EXAMNO,GNUMBER,AB_Code,EXAMYEAR,SEASON,exam_date,Short_Qual_Desc,SubjectDescription,GRADE,POINTS_1618,CAPPED_PTS,ANCN,ADFECN,BRDSUBNO,MAPPING,Level3QualificationCategory,EMQualificationCategory,R_INCL,R_INCL_EM,QUAL_KS4,UKPRN,URN,LAESTAB,cypmd_pk\n" +
            // DOB in the specification's own example format: 23 characters, though the SQL column
            // was 10. The schemas set no lengths, so it loads.
            "9900000001,500001,Smith,Alice,F,2007-01-01 00:00:00.000,18,1,60149589,Pearson,2025,S,15/06/2025,GCE A,Art and Design,A,40.00,0.00,12345,8604070,9FA0,5010,A level,,51,57,60,10000001,142313,8604070,pk1\n";
        var windowId = await SeedWindowAsync(("ingress/inc.csv", csv), ("schema/results.json", schema));

        var last = await RunAsync(windowId,
        [
            new(ResultsFileTags.Post16Included, "inc.csv", Checksum(csv), "results.json", Checksum(schema),
                Included: null, SourceFile: ResultsFileTags.Post16Included)
        ]);
        Assert.False(last.IsError, last.Message);

        var result = Assert.Single(await ResultsClient().GetResultsAsync(windowId, Laestab, "500001"));
        Assert.Equal("60149589", result.Qan);
        Assert.Equal("GCE A Art and Design", result.QualificationName);
        Assert.Equal("9FA0", result.SyllabusCode);
        Assert.Equal("S2025", result.Session);
        Assert.Equal("A", result.Grade);
        Assert.Equal(ResultsFileTags.Post16Included, result.SourceFile);
    }

    [Fact]
    public async Task A_named_column_is_kept_when_the_csv_already_has_it()
    {
        // x-ingress.source fills a property only when the CSV does not carry it by that name, so a
        // file already in the output shape is unchanged.
        const string schema = """
        {
          "type": "object",
          "properties": {
            "CYPMD_ID": { "type": ["string", "null"] },
            "QAN":      { "type": ["string", "null"], "x-ingress": { "source": "GNUMBER" } },
            "SOURCE":   { "type": "string" }
          }
        }
        """;
        const string csv = "CYPMD_ID,QAN,GNUMBER,LAESTAB\n500001,KEEP,IGNORED,8604070\n";
        var windowId = await SeedWindowAsync(("ingress/main.csv", csv), ("schema/results.json", schema));

        var last = await RunAsync(windowId,
        [
            new(ResultsFileTags.Post16Included, "main.csv", Checksum(csv), "results.json", Checksum(schema),
                Included: null, SourceFile: ResultsFileTags.Post16Included)
        ]);
        Assert.False(last.IsError, last.Message);

        Assert.Equal("KEEP", Assert.Single(await ResultsClient().GetResultsAsync(windowId, Laestab, "500001")).Qan);
    }

    [Fact]
    public async Task Every_row_is_stamped_with_the_tag_of_the_file_it_came_from()
    {
        // Provenance by file of origin. The result picker's file label and the Results tab's
        // source column read this, and no CSV supplies it.
        var windowId = await SeedWindowAsync();

        await RunAsync(windowId);

        var sources = (await ResultsClient().GetAllResultsAsync(windowId, Laestab)).Select(r => r.SourceFile).ToHashSet();
        Assert.Equal([ResultsFileTags.Post16LateResults1, ResultsFileTags.Post16Main], sources.Order());
    }

    [Fact]
    public async Task A_students_late_result_joins_their_main_results_rather_than_replacing_them()
    {
        // Both files carry the same student and the same QAN, distinguished only by source. One
        // file per school means the merge happens in the blob, not in the reader.
        var windowId = await SeedWindowAsync();

        await RunAsync(windowId);

        var results = await ResultsClient().GetResultsAsync(windowId, Laestab, "500001");

        Assert.Equal(2, results.Select(r => r.CompositeKey).Distinct().Count());
        Assert.Equal("6", results.Single(r => r.SourceFile == ResultsFileTags.Post16LateResults1).Grade);
    }

    [Fact]
    public async Task The_run_writes_the_results_name_and_leaves_no_pupil_blob_behind()
    {
        var windowId = await SeedWindowAsync();

        await RunAsync(windowId);

        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        Assert.True((await container
            .GetBlobClient(CheckingExerciseBlobPaths.ResultsBlobName(Laestab)).ExistsAsync()).Value);
        Assert.False((await container
            .GetBlobClient(CheckingExerciseBlobPaths.PupilsBlobName(CheckingExerciseType.PupilData, Laestab))
            .ExistsAsync()).Value);
    }

    [Fact]
    public async Task A_file_with_no_LAESTAB_column_fails_the_run_by_name()
    {
        // The rows cannot be split into schools at all, so the run has to stop. Naming the file is
        // the whole value of the message: six files are uploaded and one of them is wrong.
        const string noLaestab = "CYPMD_ID,QAN,QUAL_NAME,SYLLABUS,SESSION,GRADE\n500001,6037116X,Bus,1BS0,S2024,5\n";
        var windowId = await SeedWindowAsync(
            ("ingress/main.csv", noLaestab), ("schema/results.json", Schema));

        var last = await RunAsync(windowId,
        [
            new(ResultsFileTags.Post16Main, "main.csv", Checksum(noLaestab), "results.json", Checksum(Schema),
                Included: null, SourceFile: ResultsFileTags.Post16Main)
        ]);

        Assert.True(last.IsError);
        Assert.Contains("main.csv", last.Message);
        Assert.Contains("LAESTAB", last.Message);
    }

    [Fact]
    public async Task Nothing_is_stamped_when_the_schema_has_no_SOURCE_column()
    {
        // The stamp is guarded by the schema, so a dataset carrying a tag cannot break a run whose
        // schema forbids additional properties — the same guard INCLUDED has.
        const string schemaWithoutSource = """
        {
          "type": "object",
          "properties": {
            "CYPMD_ID": { "type": ["string", "null"] },
            "GRADE":    { "type": ["string", "null"] }
          }
        }
        """;
        var windowId = await SeedWindowAsync(
            ("ingress/main.csv", MainCsv), ("schema/results.json", schemaWithoutSource));

        var last = await RunAsync(windowId,
        [
            new(ResultsFileTags.Post16Main, "main.csv", Checksum(MainCsv), "results.json",
                Checksum(schemaWithoutSource), Included: null, SourceFile: ResultsFileTags.Post16Main)
        ]);

        Assert.False(last.IsError, last.Message);

        var container = _blobs.GetBlobContainerClient(windowId.ToString());
        var content = (await container
            .GetBlobClient(CheckingExerciseBlobPaths.ResultsBlobName(Laestab)).DownloadContentAsync())
            .Value.Content.ToString();

        Assert.DoesNotContain("SOURCE", content, StringComparison.Ordinal);
        Assert.Equal(2, JsonDocument.Parse(content).RootElement.GetArrayLength());
    }
}
