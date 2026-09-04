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

    // ── NameMatchesSplitQuery helper (T001) ──────────────────────────────────

    [Fact]
    public void NameMatchesSplitQuery_matches_firstname_and_surname_parts()
    {
        Assert.True(PupilSuggestionFormat.NameMatchesSplitQuery("John", "Smith", "John Smith"));
    }

    [Fact]
    public void NameMatchesSplitQuery_matches_partial_firstname_and_surname()
    {
        Assert.True(PupilSuggestionFormat.NameMatchesSplitQuery("Johnny", "Smithson", "john sm"));
    }

    [Fact]
    public void NameMatchesSplitQuery_is_case_insensitive()
    {
        Assert.True(PupilSuggestionFormat.NameMatchesSplitQuery("John", "Smith", "JOHN smith"));
    }

    [Fact]
    public void NameMatchesSplitQuery_rejects_when_surname_part_does_not_match()
    {
        Assert.False(PupilSuggestionFormat.NameMatchesSplitQuery("John", "Jones", "John Smith"));
    }

    [Fact]
    public void NameMatchesSplitQuery_rejects_when_firstname_part_does_not_match()
    {
        Assert.False(PupilSuggestionFormat.NameMatchesSplitQuery("Jane", "Smith", "John Smith"));
    }

    [Theory]
    [InlineData("John ")]       // trailing space
    [InlineData(" John Smith")] // leading space
    [InlineData("John  Smith")] // double space
    public void NameMatchesSplitQuery_handles_whitespace_variations(string query)
    {
        Assert.True(PupilSuggestionFormat.NameMatchesSplitQuery("John", "Smith", query));
    }

    [Fact]
    public void NameMatchesSplitQuery_degrades_to_single_term_when_no_space()
    {
        Assert.True(PupilSuggestionFormat.NameMatchesSplitQuery("John", "Smith", "John"));
        Assert.True(PupilSuggestionFormat.NameMatchesSplitQuery("John", "Smith", "Smith"));
    }

    [Fact]
    public void NameMatchesSplitQuery_returns_false_for_empty_query()
    {
        Assert.False(PupilSuggestionFormat.NameMatchesSplitQuery("John", "Smith", ""));
    }

    // ── T005: Two-part split matching via Matches ────────────────────────────

    [Theory]
    [InlineData("John Smith")]
    [InlineData("john smith")]
    [InlineData("JOHN SMITH")]
    public void Matches_two_part_query_matches_both_name_parts_case_insensitive(string query)
    {
        var pupil = Ks4(firstname: "John", surname: "Smith");
        Assert.True(PupilSuggestionFormat.Matches(pupil, query, CheckingWindowType.KS4June));
    }

    [Theory]
    [InlineData("john sm")]
    [InlineData("JOHN SMI")]
    public void Matches_two_part_query_matches_partial_parts(string query)
    {
        var pupil = Ks4(firstname: "Johnny", surname: "Smithson");
        Assert.True(PupilSuggestionFormat.Matches(pupil, query, CheckingWindowType.KS4June));
    }

    [Theory]
    [InlineData("Jane Smith")]   // surname matches, firstname does not
    [InlineData("John Jones")]   // firstname matches, surname does not
    public void Matches_two_part_query_excludes_when_only_one_part_matches(string query)
    {
        var pupil = Ks4(firstname: "John", surname: "Smith");
        Assert.False(PupilSuggestionFormat.Matches(pupil, query, CheckingWindowType.KS4June));
    }

    // ── T006: Edge cases ────────────────────────────────────────────────────

    [Fact]
    public void Matches_trailing_space_treats_as_single_term()
    {
        var pupil = Ks4(firstname: "John", surname: "Smith");
        Assert.True(PupilSuggestionFormat.Matches(pupil, "John ", CheckingWindowType.KS4June));
    }

    [Fact]
    public void Matches_leading_space_is_trimmed_before_split()
    {
        var pupil = Ks4(firstname: "John", surname: "Smith");
        Assert.True(PupilSuggestionFormat.Matches(pupil, " John Smith", CheckingWindowType.KS4June));
    }

    [Fact]
    public void Matches_double_space_splits_at_first_space()
    {
        var pupil = Ks4(firstname: "John", surname: "Smith");
        Assert.True(PupilSuggestionFormat.Matches(pupil, "John  Smith", CheckingWindowType.KS4June));
    }

    [Fact]
    public void Matches_three_names_splits_at_first_space()
    {
        var pupil = Ks4(firstname: "John", surname: "Smith");
        // "John Michael Smith" → first part "John", second part "Michael Smith"
        // surname.Contains("Michael Smith") is false → no match
        Assert.False(PupilSuggestionFormat.Matches(pupil, "John Michael Smith", CheckingWindowType.KS4June));
    }

    [Fact]
    public void Matches_three_names_matches_when_parts_contain()
    {
        var pupil = Ks4(firstname: "John", surname: "Michael Smith");
        Assert.True(PupilSuggestionFormat.Matches(pupil, "John Michael", CheckingWindowType.KS4June));
    }

    [Fact]
    public void Matches_all_spaces_returns_false_for_empty_after_trim()
    {
        var pupil = Ks4(firstname: "John", surname: "Smith");
        // All spaces → trimmed to empty → no match
        Assert.False(PupilSuggestionFormat.Matches(pupil, "   ", CheckingWindowType.KS4June));
    }

    // ── T007: Single-term backward compatibility (no regression) ────────────

    [Theory]
    [InlineData("A8604070001B")]   // UPN
    [InlineData("A8604")]          // UPN prefix
    [InlineData("Smi")]            // surname partial
    [InlineData("smi")]            // surname partial, case-insensitive
    [InlineData("Jan")]            // firstname partial
    [InlineData("000001")]         // CYPMD ID
    public void Matches_single_term_still_works_for_ks4(string query)
    {
        var pupil = Ks4(upn: "A8604070001B");
        Assert.True(PupilSuggestionFormat.Matches(pupil, query, CheckingWindowType.KS4June));
    }

    [Theory]
    [InlineData("9900000001")]     // ULN
    [InlineData("990")]            // ULN prefix
    [InlineData("500001")]         // CYPMD ID
    [InlineData("5000")]           // CYPMD ID prefix
    [InlineData("12/03/2007")]     // DOB display
    [InlineData("12/03")]          // DOB partial
    [InlineData("Bil")]            // firstname partial
    [InlineData("b")]              // surname
    public void Matches_single_term_still_works_for_post16(string query)
    {
        var pupil = Post16();
        Assert.True(PupilSuggestionFormat.Matches(pupil, query, CheckingWindowType.Post16));
    }

    [Fact]
    public void Matches_two_part_query_does_not_match_only_one_part_in_post16()
    {
        var pupil = Post16(firstname: "John", surname: "Smith");
        Assert.False(PupilSuggestionFormat.Matches(pupil, "John Jones", CheckingWindowType.Post16));
    }
}
