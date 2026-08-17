using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// AB#297004: the 16-19 pupil search. The suggestion has to carry enough identity for a user to tell
// apart two pupils with the same name, and has to be findable by everything a school actually knows
// about a pupil — name, ULN, CYPMD ID or date of birth.
public sealed class PupilSuggestionFormatTests
{
    private static Post16PupilRecord Post16(
        string firstname = "Billy", string surname = "B", string cypmdId = "500001",
        string uln = "9900000001", string dob = "2007-03-12 00:00:00.0000000", bool included = true) => new()
        {
            Id = Guid.NewGuid(),
            Firstname = firstname,
            Surname = surname,
            Cypmd_Id = cypmdId,
            Uln = uln,
            DateOfBirth = dob,
            Included = included
        };

    private static PupilRecord Ks4(
        string firstname = "Jane", string surname = "Smith",
        string upn = "A8604070001B", string dob = "01/01/2010") => new()
        {
            Id = Guid.NewGuid(),
            Firstname = firstname,
            Surname = surname,
            Upn = upn,
            DateOfBirth = dob,
            Cypmd_Id = "000001"
        };

    // ── Label ────────────────────────────────────────────────────────────────

    [Fact]
    public void The_post16_label_carries_forename_surname_and_every_identifier()
    {
        // AB#297004 asks for UPN here; 16-19 pupils have a ULN and no UPN, so the label says ULN.
        // FLAGGED to the BA — the ticket's wording predates that distinction.
        var label = PupilSuggestionFormat.Label(Post16(), CheckingWindowType.Post16);

        Assert.Equal("Billy, B, (CYPMD ID:500001, ULN:9900000001, DOB:12/03/2007, INCLUDED)", label);
    }

    [Fact]
    public void The_post16_label_marks_a_pupil_from_the_non_included_file()
    {
        // A pupil missing from the included data can still hold a wrong grade, so both populations
        // are searchable — and the user has to be able to see which is which.
        var label = PupilSuggestionFormat.Label(Post16(included: false), CheckingWindowType.Post16);

        Assert.EndsWith("DOB:12/03/2007, NOT INCLUDED)", label);
    }

    [Fact]
    public void The_post16_label_normalises_a_supplier_timestamp_date_of_birth()
    {
        var label = PupilSuggestionFormat.Label(
            Post16(dob: "2007-03-12 00:00:00.0000000"), CheckingWindowType.Post16);

        Assert.Contains("DOB:12/03/2007", label);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    public void Other_window_types_keep_the_existing_label(CheckingWindowType windowType)
    {
        // The KS4 journeys are live; their suggestion text must not change under this ticket.
        var label = PupilSuggestionFormat.Label(Ks4(), windowType);

        Assert.Equal("Smith, Jane, 01/01/2010", label);
    }

    // ── Matching ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bil")]              // forename, partial
    [InlineData("bil")]              // case-insensitive
    [InlineData("B")]                // surname
    [InlineData("500001")]           // CYPMD ID, exact
    [InlineData("5000")]             // CYPMD ID, prefix
    [InlineData("9900000001")]       // ULN
    [InlineData("990")]              // ULN prefix
    [InlineData("12/03/2007")]       // DOB, as displayed
    [InlineData("12/03")]            // DOB, partial
    public void A_post16_pupil_is_found_by_name_identifier_cypmd_id_or_date_of_birth(string query)
        => Assert.True(PupilSuggestionFormat.Matches(Post16(), query, CheckingWindowType.Post16));

    [Theory]
    [InlineData("Zeta")]
    [InlineData("600001")]
    [InlineData("01/01/2007")]
    public void A_post16_pupil_that_matches_nothing_is_excluded(string query)
        => Assert.False(PupilSuggestionFormat.Matches(Post16(), query, CheckingWindowType.Post16));

    [Fact]
    public void The_date_of_birth_is_matched_in_its_displayed_form_not_the_raw_supplier_form()
    {
        // The user types what they see. The raw value is a timestamp nobody would type.
        var pupil = Post16(dob: "2007-03-12 00:00:00.0000000");

        Assert.True(PupilSuggestionFormat.Matches(pupil, "12/03/2007", CheckingWindowType.Post16));
        Assert.False(PupilSuggestionFormat.Matches(pupil, "2007-03-12", CheckingWindowType.Post16));
    }

    [Fact]
    public void Date_of_birth_matching_is_post16_only_so_ks4_search_behaviour_is_unchanged()
    {
        // Widening KS4 search is outside this ticket; keeping it out means no live journey changes.
        var pupil = Ks4(dob: "01/01/2010");

        Assert.False(PupilSuggestionFormat.Matches(pupil, "01/01/2010", CheckingWindowType.KS4June));
        Assert.True(PupilSuggestionFormat.Matches(pupil, "Smi", CheckingWindowType.KS4June));
        Assert.True(PupilSuggestionFormat.Matches(pupil, "A8604070001B", CheckingWindowType.KS4June));
        Assert.True(PupilSuggestionFormat.Matches(pupil, "000001", CheckingWindowType.KS4June));
    }

    [Fact]
    public void A_pupil_with_no_date_of_birth_does_not_throw()
        => Assert.False(PupilSuggestionFormat.Matches(Post16(dob: string.Empty), "12/03", CheckingWindowType.Post16));
}
