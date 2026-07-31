using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

[Collection(nameof(AzuriteCollection))]
public sealed class PupilDataBlobClientListTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task ListSchoolLaestabsAsync_ReturnsLaestabsFromBlobNames()
    {
        var windowId = Guid.NewGuid();
        var service = new BlobServiceClient(azurite.ConnectionString);
        var container = service.GetBlobContainerClient(windowId.ToString());
        await container.CreateAsync();
        await container.UploadBlobAsync("data/8604070_pupils.json", BinaryData.FromString("[]"));
        await container.UploadBlobAsync("data/9334290_pupils.json", BinaryData.FromString("[]"));
        // Non-pupil blobs in the container must be ignored.
        await container.UploadBlobAsync("data/readme.txt", BinaryData.FromString("x"));
        await container.UploadBlobAsync("schema/schema.json", BinaryData.FromString("{}"));

        var laestabs = await new PupilDataBlobClient(service).ListSchoolLaestabsAsync(windowId);

        Assert.Equal(new[] { "8604070", "9334290" }, laestabs.OrderBy(l => l).ToArray());
    }

    [Fact]
    public async Task ListSchoolLaestabsAsync_NormalisesLaestabsThatAreNotDigitsOnly()
    {
        // The ingress writes the LAESTAB column through verbatim, so a supplier file carrying
        // "860/4070" or a padded value produces a blob name that would not join against the
        // digits-only laestab on an OrganisationLogin row. The listing is the documented
        // digits-only side of that join, so it must normalise.
        var windowId = Guid.NewGuid();
        var service = new BlobServiceClient(azurite.ConnectionString);
        var container = service.GetBlobContainerClient(windowId.ToString());
        await container.CreateAsync();
        await container.UploadBlobAsync("data/860/4070_pupils.json", BinaryData.FromString("[]"));

        var laestabs = await new PupilDataBlobClient(service).ListSchoolLaestabsAsync(windowId);

        Assert.Equal(["8604070"], laestabs);
    }

    [Fact]
    public async Task ListSchoolLaestabsAsync_MissingContainer_ReturnsEmpty()
    {
        var service = new BlobServiceClient(azurite.ConnectionString);

        var laestabs = await new PupilDataBlobClient(service).ListSchoolLaestabsAsync(Guid.NewGuid());

        Assert.Empty(laestabs);
    }
}
