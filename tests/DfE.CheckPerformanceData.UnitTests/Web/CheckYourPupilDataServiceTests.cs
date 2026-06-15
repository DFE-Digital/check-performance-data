using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public class CheckYourPupilDataServiceTests
{
    private const string TestUrn = "123456";
    private const string TestLaestab = "123/4567";
    private readonly ICheckYourPupilDataRepository _repository = Substitute.For<ICheckYourPupilDataRepository>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly CheckYourPupilDataService _sut;

    public CheckYourPupilDataServiceTests()
    {
        _currentUserService.OrganisationUrn.Returns(TestUrn);
        _currentUserService.OrganisationLaestab.Returns(TestLaestab);
        _sut = new CheckYourPupilDataService(_repository, _currentUserService);
    }

    [Fact]
    public async Task GetPupilAsync_PassesOrgLaestabToRepository()
    {
        var windowId = Guid.NewGuid();
        var pupilId = Guid.NewGuid();
        var expected = new PupilDto { Id = pupilId, Firstname = "Jane", Surname = "Smith", Sex = "F", DateOfBirth = "01/01/2010", Age = 16, Cypmd_Id = "CYPMD1", Upn = "U123" };
        _repository.GetPupilAsync(windowId, TestLaestab, pupilId).Returns(expected);

        var result = await _sut.GetPupilAsync(windowId, pupilId);

        Assert.Equal(pupilId, result.Id);
        await _repository.Received(1).GetPupilAsync(windowId, TestLaestab, pupilId);
    }

    [Fact]
    public async Task GetPupilSuggestionsAsync_PassesLaestabAndUrnToRepository()
    {
        var windowId = Guid.NewGuid();
        _repository.SearchPupilsAsync(windowId, TestLaestab, TestUrn, "smith", PupilFilter.Included, null)
            .Returns([]);

        await _sut.GetPupilSuggestionsAsync(windowId, "smith", PupilFilter.Included);

        await _repository.Received(1)
            .SearchPupilsAsync(windowId, TestLaestab, TestUrn, "smith", PupilFilter.Included, null);
    }
}
