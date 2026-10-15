using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// Every tab page reads each exercise's files and schemas, so the reader caches what can never
// change (a release's output, an upload under its checksum folder) and reads everything else from
// storage each time. The tests overwrite a blob in place: a cached path must not see the change,
// and an uncached one must.
[Collection(nameof(AzuriteCollection))]
public sealed class CheckingDataReaderCachingTests(AzuriteFixture azurite)
{
    private const string Laestab = "8604070";

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();
    private readonly BlobServiceClient _service = new(azurite.ConnectionString);

    private CheckingDataExercise Exercise(Guid? releaseId) => new(_exerciseId, _windowId, "Pupils", "Pupils", 0,
        CheckingExerciseType.PupilData, KeyStages.KS4, true, null, null, DateTime.Today, DateTime.Today,
        DateTime.Today, DateTime.Today, null, true, false, releaseId);

    private async Task WriteAsync(string name, string content)
    {
        var container = _service.GetBlobContainerClient(_windowId.ToString());
        await container.CreateIfNotExistsAsync();
        await container.GetBlobClient(name).UploadAsync(BinaryData.FromString(content), overwrite: true);
    }

    private CheckingDataReader Reader() => new(_service, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task A_releases_output_is_read_from_storage_once()
    {
        var releaseId = Guid.NewGuid();
        var name = CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, Laestab, releaseId);
        await WriteAsync(name, "[1]");
        var reader = Reader();

        Assert.Equal("[1]", System.Text.Encoding.UTF8.GetString((await reader.ReadAsync(Exercise(releaseId), Laestab, default))!));
        await WriteAsync(name, "[2]");

        Assert.Equal("[1]", System.Text.Encoding.UTF8.GetString((await reader.ReadAsync(Exercise(releaseId), Laestab, default))!));
    }

    [Fact]
    public async Task The_unversioned_output_is_read_from_storage_every_time()
    {
        // The seeders write here in place, so a cached copy could go stale.
        var name = CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, Laestab);
        await WriteAsync(name, "[1]");
        var reader = Reader();

        await reader.ReadAsync(Exercise(null), Laestab, default);
        await WriteAsync(name, "[2]");

        Assert.Equal("[2]", System.Text.Encoding.UTF8.GetString((await reader.ReadAsync(Exercise(null), Laestab, default))!));
    }

    [Fact]
    public async Task A_schema_under_its_checksum_folder_is_cached_and_one_without_is_not()
    {
        var datasetId = Guid.NewGuid();
        var withChecksum = CheckingExerciseBlobPaths.DefinitionFile(_exerciseId, datasetId, "abc123", "schema.json");
        var withoutChecksum = CheckingExerciseBlobPaths.DefinitionFile(_exerciseId, datasetId, "", "schema.json");
        await WriteAsync(withChecksum, "{\"v\":1}");
        await WriteAsync(withoutChecksum, "{\"v\":1}");
        var reader = Reader();

        await reader.ReadSchemaAsync(_windowId, withChecksum, default);
        await reader.ReadSchemaAsync(_windowId, withoutChecksum, default);
        await WriteAsync(withChecksum, "{\"v\":2}");
        await WriteAsync(withoutChecksum, "{\"v\":2}");

        Assert.Equal("{\"v\":1}", System.Text.Encoding.UTF8.GetString((await reader.ReadSchemaAsync(_windowId, withChecksum, default))!));
        Assert.Equal("{\"v\":2}", System.Text.Encoding.UTF8.GetString((await reader.ReadSchemaAsync(_windowId, withoutChecksum, default))!));
    }

    [Fact]
    public async Task A_missing_blob_or_container_reads_as_null()
    {
        Assert.Null(await Reader().ReadAsync(Exercise(Guid.NewGuid()), Laestab, default));
        Assert.Null(await Reader().ReadSchemaAsync(Guid.NewGuid(), "missing.json", default));
    }

    [Fact]
    public async Task HasSchoolData_finds_a_dataset_file_of_the_current_release()
    {
        var releaseId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        await WriteAsync(CheckingExerciseBlobPaths.DatasetBlobName(_exerciseId, releaseId, datasetId, Laestab), "[]");
        var exercise = new CheckingExerciseDto
        {
            Id = _exerciseId, ExerciseType = null, DisplayOnly = true, UsesExerciseStorage = true,
            StartDate = DateTime.Today, EndDate = DateTime.Today, CurrentReleaseId = releaseId,
            Releases =
            [
                new CheckingExerciseReleaseDto
                {
                    Id = releaseId,
                    Files = [new CheckingExerciseReleaseFileDto { DatasetId = datasetId, DatasetName = "summary" }]
                }
            ]
        };

        Assert.True(await Reader().HasSchoolDataAsync(_windowId, exercise, Laestab, default));
        Assert.False(await Reader().HasSchoolDataAsync(_windowId, exercise, "1111111", default));
    }
}
