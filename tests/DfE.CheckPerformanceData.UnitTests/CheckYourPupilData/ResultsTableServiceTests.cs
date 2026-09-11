using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// The Results tab: one row per main-file result, joined to the pupil file on CYPMD ID. The tab
// exists only for a window that runs a results enquiry whose type has a main results slot.
public sealed class ResultsTableServiceTests
{
    private const string Laestab = "860/4070";
    private static readonly Guid WindowId = Guid.NewGuid();

    private readonly ICheckYourPupilDataRepository _repository = Substitute.For<ICheckYourPupilDataRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IStudentResultsClient _results = Substitute.For<IStudentResultsClient>();
    private readonly CheckYourPupilDataService _sut;

    public ResultsTableServiceTests()
    {
        _currentUser.OrganisationLaestab.Returns(Laestab);
        _sut = new CheckYourPupilDataService(_repository, _currentUser, _results);
        Window(CheckingWindowType.Post16, CheckingExerciseType.PupilData, CheckingExerciseType.ResultsEnquiry);
        _repository.GetAllPupilsForSchoolAsync(WindowId, Laestab).Returns(new List<IPupilRecord>
        {
            Pupil("500001", "Smith", "Alice"),
            Pupil("500002", "Jones", "Bob")
        });
        _results.GetResultsForSourceAsync(WindowId, Laestab, ResultsFileTags.Post16Main).Returns(new List<StudentResultRecord>
        {
            Result("500002", "Maths"),
            Result("500001", "French"),
            Result("500001", "Art"),
            Result("999999", "Physics")
        });
    }

    private void Window(CheckingWindowType type, params CheckingExerciseType[] exercises) =>
        _repository.GetCheckingWindowAsync(WindowId).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "Test",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = type,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow,
            Exercises = exercises.Select(e => new CheckingExerciseDto
            {
                ExerciseType = e, StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow
            }).ToList()
        });

    private static Post16PupilRecord Pupil(string cypmdId, string surname, string firstname) => new()
    {
        Id = Guid.NewGuid(), Included = true, Cypmd_Id = cypmdId, Surname = surname, Firstname = firstname,
        Sex = "F", DateOfBirth = "2007-09-01", Age = 18, Laestab = Laestab, Urn = "1", Ukprn = "1", Uln = "1"
    };

    private static StudentResultRecord Result(string cypmdId, string subject, string source = ResultsFileTags.Post16Main) => new()
    {
        CypmdId = cypmdId, Qan = "Q", QualificationName = subject, Session = "S2024", Grade = "5", SourceFile = source
    };

    [Fact]
    public async Task Null_when_the_window_runs_no_results_enquiry()
    {
        Window(CheckingWindowType.Post16, CheckingExerciseType.PupilData);

        Assert.Null(await _sut.GetResultsTableAsync(WindowId, null, 0, 10));
        Assert.Null(await _sut.GetResultsCsvAsync(WindowId));
    }

    [Fact]
    public async Task Null_when_the_window_type_has_no_main_results_slot()
    {
        // KS2 has no results feed, so a results enquiry ticked on one has nothing to list.
        Window(CheckingWindowType.KS2, CheckingExerciseType.ResultsEnquiry);

        Assert.Null(await _sut.GetResultsTableAsync(WindowId, null, 0, 10));
    }

    [Fact]
    public async Task Reads_only_the_main_file_for_the_window_type()
    {
        await _sut.GetResultsTableAsync(WindowId, null, 0, 10);

        await _results.Received(1).GetResultsForSourceAsync(WindowId, Laestab, ResultsFileTags.Post16Main);
    }

    [Fact]
    public async Task Ks4_window_reads_the_ks4_main_file()
    {
        Window(CheckingWindowType.KS4Autumn, CheckingExerciseType.ResultsEnquiry);

        await _sut.GetResultsTableAsync(WindowId, null, 0, 10);

        await _results.Received(1).GetResultsForSourceAsync(WindowId, Laestab, ResultsFileTags.Ks4Main);
    }

    [Fact]
    public async Task Rows_are_joined_on_cypmd_id_and_sorted_by_surname_first_name_then_subject()
    {
        var (table, total) = (await _sut.GetResultsTableAsync(WindowId, null, 0, 10))!.Value;

        Assert.Equal(4, total);
        Assert.Equal(
        [
            ["", "", "", "", "", "999999", "Physics"],   // unmatched: blank names sort first
            ["Jones", "Bob", "F", "01/09/2007", "18", "500002", "Maths"],
            ["Smith", "Alice", "F", "01/09/2007", "18", "500001", "Art"],
            ["Smith", "Alice", "F", "01/09/2007", "18", "500001", "French"]
        ], table.Rows.Select(r => r.ToArray()).ToArray());
    }

    [Fact]
    public async Task Join_is_case_insensitive_on_cypmd_id()
    {
        _results.GetResultsForSourceAsync(WindowId, Laestab, ResultsFileTags.Post16Main)
            .Returns(new List<StudentResultRecord> { Result("500001", "Art") });
        _repository.GetAllPupilsForSchoolAsync(WindowId, Laestab)
            .Returns(new List<IPupilRecord> { Pupil("500001".ToUpperInvariant(), "Smith", "Alice") });

        var (table, _) = (await _sut.GetResultsTableAsync(WindowId, null, 0, 10))!.Value;

        Assert.Equal("Smith", Assert.Single(table.Rows)[0]);
    }

    [Theory]
    [InlineData("smi", 2)]      // surname contains
    [InlineData("BOB", 1)]      // first name contains, case-insensitive
    [InlineData("5000", 3)]     // CYPMD ID starts with
    [InlineData("0001", 0)]     // CYPMD ID is StartsWith, not Contains
    [InlineData("phys", 1)]     // subject contains
    public async Task Search_matches_name_cypmd_id_or_subject(string search, int expected)
    {
        var (_, total) = (await _sut.GetResultsTableAsync(WindowId, search, 0, 10))!.Value;

        Assert.Equal(expected, total);
    }

    [Fact]
    public async Task Pages_the_sorted_rows_and_reports_the_unpaged_total()
    {
        var (table, total) = (await _sut.GetResultsTableAsync(WindowId, null, 1, 3))!.Value;

        Assert.Equal(4, total);
        Assert.Equal("French", Assert.Single(table.Rows)[6]);
    }

    [Fact]
    public async Task Csv_carries_every_row_with_the_csv_columns()
    {
        var table = await _sut.GetResultsCsvAsync(WindowId);

        Assert.Equal(4, table!.Rows.Count);
        Assert.Equal("Source file", table.Headers[^1]);
        Assert.Equal(ResultsFileTags.Post16Main, table.Rows[0][^1]);
    }
}
