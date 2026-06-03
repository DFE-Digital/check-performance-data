using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public class CheckYourPupilDataServiceTests
{
    private const string TestUrn = "123456";
    private readonly ICheckYourPupilDataRepository _repository = Substitute.For<ICheckYourPupilDataRepository>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly CheckYourPupilDataService _sut;

    public CheckYourPupilDataServiceTests()
    {
        _currentUserService.OrganisationUrn.Returns(TestUrn);
        _sut = new CheckYourPupilDataService(_repository, _currentUserService);
    }

    [Fact]
    public async Task GetPupilAsync_PassesOrgUrnToRepository()
    {
        var windowId = Guid.NewGuid();
        var pupilId = Guid.NewGuid();
        var expected = new PupilDto { Id = pupilId, Firstname = "Jane", Surname = "Smith", Sex = "F", DateOfBirth = "01/01/2010", Age = 16, Cypmd_Id = "CYPMD1", Upn = "U123" };
        _repository.GetPupilAsync(windowId, TestUrn, pupilId).Returns(expected);

        var result = await _sut.GetPupilAsync(windowId, pupilId);

        Assert.Equal(pupilId, result.Id);
        await _repository.Received(1).GetPupilAsync(windowId, TestUrn, pupilId);
    }
}
