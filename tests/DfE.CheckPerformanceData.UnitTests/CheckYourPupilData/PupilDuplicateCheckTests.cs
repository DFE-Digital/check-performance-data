using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// AB#297780 Add Pupil duplicate check. The Add journey asks a school for a pupil who is not on
// their roll; before minting a synthetic pupil the service checks the whole school-window roll
// (included + non-included) for an existing pupil with the same first name, surname and DOB.
public sealed class PupilDuplicateCheckTests
{
    private const string TestLaestab = "123/4567";
    private readonly ICheckYourPupilDataRepository _repository = Substitute.For<ICheckYourPupilDataRepository>();
    private readonly ICurrentUserService _currentUserService = Substitute.For<ICurrentUserService>();
    private readonly CheckYourPupilDataService _sut;

    public PupilDuplicateCheckTests()
    {
        _currentUserService.OrganisationUrn.Returns("123456");
        _currentUserService.OrganisationLaestab.Returns(TestLaestab);
        _sut = new CheckYourPupilDataService(_repository, _currentUserService, Substitute.For<IStudentResultsClient>());
    }

    private static PupilRecord Ks4Pupil(
        string firstname = "Alice", string surname = "Smith", string dob = "2010-09-01",
        string upn = "A860407000001B", bool included = true) => new()
    {
        Id = Guid.NewGuid(),
        Upn = upn,
        Cypmd_Id = "000123",
        Surname = surname,
        Firstname = firstname,
        Sex = "F",
        DateOfBirth = dob,
        Age = 15,
        Pincl = included ? 401 : 402,
        Laestab = TestLaestab,
        Urn = 136309
    };

    // ── No match (T010) ──────────────────────────────────────────────────────

    [Fact]
    public async Task No_match_when_nothing_shares_the_first_name_surname_and_dob()
    {
        // A pupil with the same name but a different DOB is a near-miss, not a match.
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord>
            {
                Ks4Pupil(firstname: "Alice", surname: "Smith", dob: "2010-09-01")
            });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "Alice", "Smith", "2005-05-05");

        Assert.Equal(DuplicateScenario.None, result.Scenario);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task No_match_is_returned_for_an_empty_roll()
    {
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab).Returns([]);

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "Alice", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.None, result.Scenario);
        Assert.Empty(result.Matches);
    }

    // ── Matches across both populations (T011) ───────────────────────────────

    [Fact]
    public async Task Two_matches_across_included_and_non_included_are_both_returned()
    {
        var included = Ks4Pupil(included: true);
        var nonIncluded = Ks4Pupil(included: false, upn: "A860407000002C");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { included, nonIncluded });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "Alice", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.Multiple, result.Scenario);
        Assert.Equal(2, result.Matches.Count);
        Assert.Contains(result.Matches, m => m.IsIncluded);
        Assert.Contains(result.Matches, m => !m.IsIncluded);
        Assert.All(result.Matches, m => Assert.Equal("Alice", m.Firstname));
    }

    [Fact]
    public async Task A_non_included_pupil_is_searchable_and_marked_as_such()
    {
        var nonIncluded = Ks4Pupil(included: false, upn: "A860407000002C");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { nonIncluded });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "Alice", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleNonIncluded, result.Scenario);
        Assert.False(result.Matches[0].IsIncluded);
    }

    // ── US2: single non-included ⇒ Include available (T023) ─────────────────

    [Fact]
    public void A_single_non_included_match_classifies_as_include_available_not_a_list()
    {
        // The Include action is offered for exactly one non-included match (US2), and only there —
        // never for an already-included pupil and never as the multiple-match list.
        var match = new DuplicateMatch
        {
            Id = Guid.NewGuid(),
            Firstname = "Alice",
            Surname = "Smith",
            DateOfBirth = "01/09/2010",
            Identifier = "A860407000001B",
            IsIncluded = false
        };

        var result = PupilDuplicateCheckResult.Build([match]);

        Assert.Equal(DuplicateScenario.SingleNonIncluded, result.Scenario);
        Assert.NotEqual(DuplicateScenario.Multiple, result.Scenario);
        Assert.Single(result.Matches);
    }

    // ── US3: multiple matches & already-included (T030, T031) ───────────────

    [Fact]
    public void Multiple_matches_build_a_list_result_with_per_row_inclusion_status()
    {
        var included = new DuplicateMatch
        {
            Id = Guid.NewGuid(),
            Firstname = "Alice",
            Surname = "Smith",
            DateOfBirth = "01/09/2010",
            Identifier = "A860407000001B",
            IsIncluded = true
        };
        var nonIncluded = new DuplicateMatch
        {
            Id = Guid.NewGuid(),
            Firstname = "Alice",
            Surname = "Smith",
            DateOfBirth = "01/09/2010",
            Identifier = "A860407000002C",
            IsIncluded = false
        };

        var result = PupilDuplicateCheckResult.Build([included, nonIncluded]);

        Assert.Equal(DuplicateScenario.Multiple, result.Scenario);
        Assert.Equal(2, result.Matches.Count);
        Assert.Contains(result.Matches, m => m.IsIncluded);
        Assert.Contains(result.Matches, m => !m.IsIncluded);
    }

    [Fact]
    public void An_already_included_single_match_does_not_expose_the_include_action()
    {
        // A single already-included pupil is never SingleNonIncluded, so US2's Include is not
        // offered; the abort warning takes precedence (US3 / T031).
        var included = new DuplicateMatch
        {
            Id = Guid.NewGuid(),
            Firstname = "Alice",
            Surname = "Smith",
            DateOfBirth = "01/09/2010",
            Identifier = "A860407000001B",
            IsIncluded = true
        };

        var result = PupilDuplicateCheckResult.Build([included]);

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.NotEqual(DuplicateScenario.SingleNonIncluded, result.Scenario);
    }

    // ── PII-safe classification (T012) ───────────────────────────────────────

    [Fact]
    public async Task An_included_single_match_is_classified_single_included()
    {
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { Ks4Pupil(included: true) });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "Alice", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.True(result.Matches[0].IsIncluded);
    }

    [Fact]
    public async Task Names_are_matched_case_insensitively()
    {
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { Ks4Pupil() });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "aliCe", "sMiTh", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task The_repository_is_queried_for_the_whole_school_population()
    {
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab).Returns([]);
        var windowId = Guid.NewGuid();

        await _sut.DuplicateCheckAsync(windowId, "Alice", "Smith", "2010-09-01");

        await _repository.Received(1).GetAllPupilsForSchoolAsync(windowId, TestLaestab);
    }

    // ── Failure fast path (T013) ─────────────────────────────────────────────

    [Fact]
    public async Task A_query_failure_is_treated_as_no_matches_not_an_error()
    {
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Throws(new InvalidOperationException("blob unavailable"));

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "Alice", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.None, result.Scenario);
        Assert.Empty(result.Matches);
    }

    // ── NameMatchesSplitQuery in duplicate-check context (T002) ────────────

    [Fact]
    public async Task Duplicate_check_matches_firstname_and_surname_via_split_query()
    {
        var pupil = Ks4Pupil(firstname: "John", surname: "Smith");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "John", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task Duplicate_check_matches_containing_names_via_split_query()
    {
        var pupil = Ks4Pupil(firstname: "Johnny", surname: "Smithson");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "John", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task Duplicate_check_rejects_when_only_one_name_part_matches()
    {
        var pupil = Ks4Pupil(firstname: "John", surname: "Jones");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "John", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.None, result.Scenario);
        Assert.Empty(result.Matches);
    }

    // ── T010: Two-part split matching in duplicate check ────────────────────

    [Fact]
    public async Task Duplicate_check_finds_pupils_with_containing_names()
    {
        // "Johnny Smithson" contains "John" in firstname and "Smith" in surname
        var pupil = Ks4Pupil(firstname: "Johnny", surname: "Smithson");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "John", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.Single(result.Matches);
        Assert.Equal("Johnny", result.Matches[0].Firstname);
        Assert.Equal("Smithson", result.Matches[0].Surname);
    }

    [Fact]
    public async Task Duplicate_check_excludes_when_surname_part_does_not_match()
    {
        var pupil = Ks4Pupil(firstname: "Johnny", surname: "Jones");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "John", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.None, result.Scenario);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Duplicate_check_excludes_when_firstname_part_does_not_match()
    {
        var pupil = Ks4Pupil(firstname: "Jane", surname: "Smithson");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "John", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.None, result.Scenario);
        Assert.Empty(result.Matches);
    }

    // ── T011: Backward compatibility ─────────────────────────────────────────

    [Fact]
    public async Task Duplicate_check_exact_match_still_works()
    {
        // Exact match should still work via the split query's single-term fallback
        var pupil = Ks4Pupil(firstname: "Alice", surname: "Smith");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "Alice", "Smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task Duplicate_check_case_insensitive_matching_preserved_with_split()
    {
        var pupil = Ks4Pupil(firstname: "Alice", surname: "Smith");
        _repository.GetAllPupilsForSchoolAsync(Arg.Any<Guid>(), TestLaestab)
            .Returns(new List<IPupilRecord> { pupil });

        var result = await _sut.DuplicateCheckAsync(Guid.NewGuid(), "alice", "smith", "2010-09-01");

        Assert.Equal(DuplicateScenario.SingleIncluded, result.Scenario);
        Assert.Single(result.Matches);
    }
}
