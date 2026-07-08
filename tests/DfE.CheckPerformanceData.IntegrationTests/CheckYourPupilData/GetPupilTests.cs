using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

[Collection(nameof(PostgresCollection))]
public sealed class GetPupilTests(PostgresFixture fixture)
{
    private const string TestLaestab = "860/4070";
    private const int IncludedPincl = 401;

    private CheckYourPupilDataRepository CreateRepo(IPupilDataBlobClient blobClient)
        => new(fixture.CreateContext(), blobClient, new MemoryCache(new MemoryCacheOptions()));

    private static PupilRecord NewPupil(Guid windowId, Guid id) => new()
    {
        Id = id,
        CheckingWindowId = windowId,
        Urn = 142313,
        Laestab = TestLaestab,
        Surname = "Smith",
        Firstname = "Jane",
        Sex = "F",
        DateOfBirth = "01/01/2010",
        Age = 16,
        FirstLanguage = "English",
        Pincl = IncludedPincl,
        NewMobile = false,
        ActualYearGroup = "11",
        Ethnicity = "A1",
        SenF = "N",
        EntryDate = "01/09/2021",
        Cypmd_Id = Guid.NewGuid().ToString(),
        MatchRef = 1,
        Upn = $"U{Guid.NewGuid():N}"[..13],
    };

    [Fact]
    public async Task GetPupilAsync_WhenPupilPresent_ReturnsPupil()
    {
        var windowId = Guid.NewGuid();
        var pupilId = Guid.NewGuid();

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [NewPupil(windowId, pupilId)]);

        var repo = CreateRepo(blobClient);
        var result = await repo.GetPupilAsync(windowId, TestLaestab, pupilId);

        Assert.Equal(pupilId, result.Id);
    }

    [Fact]
    public async Task GetPupilAsync_WhenPupilNotInFile_Throws()
    {
        var windowId = Guid.NewGuid();

        var blobClient = new FakePupilDataBlobClient();
        blobClient.SetPupils(windowId, TestLaestab, [NewPupil(windowId, Guid.NewGuid())]);

        var repo = CreateRepo(blobClient);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.GetPupilAsync(windowId, TestLaestab, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPupilAsync_WhenSchoolHasNoFile_Throws()
    {
        var windowId = Guid.NewGuid();

        var repo = CreateRepo(new FakePupilDataBlobClient());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.GetPupilAsync(windowId, TestLaestab, Guid.NewGuid()));
    }
}
