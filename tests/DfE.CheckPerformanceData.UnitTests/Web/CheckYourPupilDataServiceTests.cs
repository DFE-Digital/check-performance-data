using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
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
        var expected = new PupilDto { Id = pupilId, Firstname = "Jane", Surname = "Smith", Sex = "F", DateOfBirth = "01/01/2010", Age = 16, Cypmd_Id = "CYPMD1", Identifier = "U123" };
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

    private void StubWindow(Guid windowId, CheckingWindowType type) =>
        _repository.GetCheckingWindowAsync(windowId).Returns(new CheckingWindowDto
        {
            Id = windowId,
            Title = "W",
            KeyStage = type == CheckingWindowType.Post16 ? KeyStages.Post16 : KeyStages.KS4,
            CheckingWindowType = type,
            StartDate = DateTime.Now.AddDays(-1),
            EndDate = DateTime.Now.AddDays(10)
        });

    private static PupilRecord Ks4Pupil() => new()
    {
        Id = Guid.NewGuid(), Upn = "A860407000001B", Cypmd_Id = "000123", Surname = "Smith",
        Firstname = "Alice", Sex = "F", DateOfBirth = "2010-09-01", Age = 15, Pincl = 401,
        Laestab = TestLaestab, Urn = 136309, EntryDate = "2021-09-01", SenF = "N",
        FirstLanguage = "ENG", Ethnicity = "WBRI", ActualYearGroup = "11", NewMobile = false
    };

    private static Post16PupilRecord Post16Pupil() => new()
    {
        Id = Guid.NewGuid(), Included = true, Cypmd_Id = "500123", Surname = "Jones",
        Firstname = "Bob", Sex = "M", DateOfBirth = "2007-09-01 00:00:00.0000000", Age = 18,
        Pincl = 501, Laestab = TestLaestab, Urn = "136309", Ukprn = "10001234", Uln = "9900112233"
    };

    // The window type decides which column set the page and CSV get. This is the join between
    // the stored record shape and what the user sees, so it is asserted at the service boundary.
    [Fact]
    public async Task GetPupilTableAsync_projects_ks4_pupils_with_the_ks4_column_set()
    {
        var windowId = Guid.NewGuid();
        StubWindow(windowId, CheckingWindowType.KS4June);
        _repository.GetPupilPageAsync(windowId, TestLaestab, true, null, 0, 10)
            .Returns((new List<IPupilRecord> { Ks4Pupil() }, 1));

        var (table, total) = await _sut.GetPupilTableAsync(windowId, included: true, null, 0, 10);

        Assert.Equal(1, total);
        Assert.Equal(["Last name", "First name", "Sex", "Date of birth", "Age", "CYPMD ID"], table.Headers);
        Assert.Equal("Smith", table.Rows[0][0]);
        Assert.Equal("01/09/2010", table.Rows[0][3]);
    }

    [Fact]
    public async Task GetPupilTableAsync_projects_post16_pupils_with_the_post16_column_set()
    {
        var windowId = Guid.NewGuid();
        StubWindow(windowId, CheckingWindowType.Post16);
        _repository.GetPupilPageAsync(windowId, TestLaestab, true, null, 0, 10)
            .Returns((new List<IPupilRecord> { Post16Pupil() }, 1));

        var (table, _) = await _sut.GetPupilTableAsync(windowId, included: true, null, 0, 10);

        Assert.Contains("ULN", table.Headers);
        Assert.Equal("9900112233", table.Rows[0][^2]);
        Assert.Equal("501", table.Rows[0][^1]);
    }

    [Fact]
    public async Task GetPupilCsvAsync_uses_the_csv_column_set_not_the_table_one()
    {
        var windowId = Guid.NewGuid();
        StubWindow(windowId, CheckingWindowType.KS4June);
        _repository.GetAllPupilsAsync(windowId, TestLaestab, true)
            .Returns(new List<IPupilRecord> { Ks4Pupil() });

        var table = await _sut.GetPupilCsvAsync(windowId, included: true);

        // The CSV carries far more than the six on-screen columns, starting with the identifier.
        Assert.Equal("UPN", table.Headers[0]);
        Assert.Contains("Pupil Inclusion description", table.Headers);
        Assert.Equal(17, table.Headers.Count);
    }

    [Fact]
    public async Task GetPupilCsvAsync_says_ULN_for_a_post16_window()
    {
        var windowId = Guid.NewGuid();
        StubWindow(windowId, CheckingWindowType.Post16);
        _repository.GetAllPupilsAsync(windowId, TestLaestab, false)
            .Returns(new List<IPupilRecord> { Post16Pupil() });

        var table = await _sut.GetPupilCsvAsync(windowId, included: false);

        Assert.Equal("ULN", table.Headers[0]);
        Assert.DoesNotContain("UPN", table.Headers);
    }
}
