using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// The extension form asks which of up to hundreds of schools have a
// file, in one listing rather than one existence call each. A release's files never change, so the
// answer is cached per release; the unversioned and legacy prefixes are read from storage each time.
[Collection(nameof(AzuriteCollection))]
public sealed class CheckingDataReaderSchoolListingTests(AzuriteFixture azurite)
{
    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();
    private readonly Guid _datasetId = Guid.NewGuid();
    private readonly BlobServiceClient _service = new(azurite.ConnectionString);

    private async Task WriteAsync(string name)
    {
        var container = _service.GetBlobContainerClient(_windowId.ToString());
        await container.CreateIfNotExistsAsync();
        await container.GetBlobClient(name).UploadAsync(BinaryData.FromString("[]"), overwrite: true);
    }

    private CheckingExerciseDto Exercise(Guid? releaseId, bool usesExerciseStorage = true, CheckingExerciseType? kind = CheckingExerciseType.PupilData) => new()
    {
        Id = _exerciseId, ExerciseType = kind, StartDate = DateTime.Today, EndDate = DateTime.Today,
        UsesExerciseStorage = usesExerciseStorage, CurrentReleaseId = releaseId
    };

    private CheckingDataReader Reader() => new(_service, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Lists_every_school_with_a_merged_file_or_a_dataset_file_in_the_current_release()
    {
        var releaseId = Guid.NewGuid();
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, "9334290", releaseId));
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, "860/4070", releaseId));
        await WriteAsync(CheckingExerciseBlobPaths.DatasetBlobName(_exerciseId, releaseId, _datasetId, "1234567"));
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, "7777777", Guid.NewGuid()));  // another release

        var schools = await Reader().ListSchoolsWithDataAsync(_windowId, Exercise(releaseId), CancellationToken.None);

        Assert.Equal(["1234567", "8604070", "9334290"], schools.Order());
    }

    [Fact]
    public async Task An_exercise_with_no_release_lists_the_unversioned_prefix_uncached()
    {
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, "9334290"));
        var reader = Reader();

        var before = await reader.ListSchoolsWithDataAsync(_windowId, Exercise(null), CancellationToken.None);
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, "8604070"));
        var after = await reader.ListSchoolsWithDataAsync(_windowId, Exercise(null), CancellationToken.None);

        Assert.Equal(["9334290"], before.Order());
        Assert.Equal(["8604070", "9334290"], after.Order());
    }

    [Fact]
    public async Task A_releases_listing_is_cached()
    {
        var releaseId = Guid.NewGuid();
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, "9334290", releaseId));
        var reader = Reader();

        var before = await reader.ListSchoolsWithDataAsync(_windowId, Exercise(releaseId), CancellationToken.None);
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(_exerciseId, CheckingDataType.Pupil, "8604070", releaseId));
        var after = await reader.ListSchoolsWithDataAsync(_windowId, Exercise(releaseId), CancellationToken.None);

        Assert.Equal(["9334290"], before.Order());
        Assert.Equal(["9334290"], after.Order());
    }

    [Fact]
    public async Task A_legacy_exercise_lists_its_kind_prefix()
    {
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(CheckingExerciseType.PupilData, "933/4290"));
        await WriteAsync(CheckingExerciseBlobPaths.DataBlobName(CheckingExerciseType.ResultsEnquiry, "860/4070"));

        var schools = await Reader().ListSchoolsWithDataAsync(_windowId, Exercise(null, usesExerciseStorage: false), CancellationToken.None);

        Assert.Equal(["9334290"], schools.Order());
    }

    [Fact]
    public async Task A_missing_container_lists_nobody()
    {
        var schools = await Reader().ListSchoolsWithDataAsync(Guid.NewGuid(), Exercise(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(schools);
    }
}
