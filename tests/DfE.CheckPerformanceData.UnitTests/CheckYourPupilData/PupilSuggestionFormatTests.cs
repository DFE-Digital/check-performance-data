using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
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
        string upn = "A8604070001B", string dob = "01/01/2010", string cypmdId = "000001",
        bool included = true) => new()
        {
            Id = Guid.NewGuid(),
            Firstname = firstname,
            Surname = surname,
            Upn = upn,
            DateOfBirth = dob,
            Cypmd_Id = cypmdId,
            Included = included
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

    // #623: the KS4 suggestion carries every identifier the hint says a school can search by,
    // plus the inclusion status, in the format UCD gave.
    private const string Ks4Label =
        "Smith, Jane, (CYPMD ID: 800001, UPN: A8604070001B, DOB: 01/01/2010, Status: INCLUDED)";

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void The_ks4_label_carries_surname_forename_and_every_identifier(CheckingWindowType windowType)
    {
        var label = PupilSuggestionFormat.Label(CypmdIdPupil, windowType);

        Assert.Equal(Ks4Label, label);
    }

    [Fact]
    public void The_ks4_label_marks_a_pupil_who_is_not_included()
    {
        var label = PupilSuggestionFormat.Label(
            Ks4(cypmdId: "800001", included: false), CheckingWindowType.KS4June);

        Assert.EndsWith("Status: NOT INCLUDED)", label);
    }

    [Fact]
    public void The_ks4_label_normalises_an_iso_date_of_birth()
    {
        var label = PupilSuggestionFormat.Label(Ks4(dob: "2010-01-01"), CheckingWindowType.KS4June);

        Assert.Contains("DOB: 01/01/2010", label);
    }

    // ── Matching — PupilSearchField.CypmdId (AB#304118) ───────────────────────
    // The KS4 merge journey's second-record page asks for "What is the CYPMD ID of the second
    // duplicate record to be merged?" and hints "Start typing ID to search records", so on that page
    // a name must not match. Everything below is one pupil record: any case that returns true is the
    // CypmdId prefix, and any that returns false is proof the other fields were dropped rather than
    // merely not exercised.

    private static readonly PupilRecord CypmdIdPupil =
        Ks4(firstname: "Jane", surname: "Smith", upn: "A8604070001B", dob: "01/01/2010", cypmdId: "800001");

    [Theory]
    [InlineData("800001")]        // the whole ID
    [InlineData("8000")]          // a leading partial is enough to find the record
    [InlineData("80")]
    [InlineData("8")]
    public void A_cypmd_id_search_matches_the_typed_prefix(string query)
    {
        Assert.True(PupilSuggestionFormat.Matches(
            CypmdIdPupil, query, CheckingWindowType.KS4June, PupilSearchField.CypmdId));
    }

    [Theory]
    [InlineData("a8604070001b")]  // the UPN, lower-cased — a different identifier, not the CYPMD ID
    [InlineData("A8604070001B")]
    [InlineData("Smith")]         // surname
    [InlineData("Jane")]          // forename
    [InlineData("Jane Smith")]    // split-name query
    [InlineData("Smith Jane")]
    [InlineData("01/01/2010")]    // date of birth, in the display format the page would show
    [InlineData("01/01")]         // a partial date of birth
    [InlineData("800002")]        // a different record's ID
    [InlineData("")]              // nothing typed
    [InlineData("   ")]           // whitespace only
    public void A_cypmd_id_search_matches_nothing_else(string query)
    {
        Assert.False(PupilSuggestionFormat.Matches(
            CypmdIdPupil, query, CheckingWindowType.KS4June, PupilSearchField.CypmdId));
    }

    [Theory]
    [InlineData("800001")]
    [InlineData("8000")]
    public void A_cypmd_id_search_ignores_case(string query)
    {
        Assert.True(PupilSuggestionFormat.Matches(
            CypmdIdPupil, query.ToUpperInvariant(), CheckingWindowType.KS4June, PupilSearchField.CypmdId));
    }

    [Fact]
    public void A_cypmd_id_search_ignores_leading_and_trailing_whitespace()
    {
        Assert.True(PupilSuggestionFormat.Matches(
            CypmdIdPupil, "  8000  ", CheckingWindowType.KS4June, PupilSearchField.CypmdId));
    }

    [Fact]
    public void A_cypmd_id_search_works_the_same_on_a_16_19_window()
    {
        // The restriction is a property of the page, not of the window type, so a 16-19 page that
        // asked for one would get the same rule.
        Assert.True(PupilSuggestionFormat.Matches(
            Post16(cypmdId: "500001"), "5000", CheckingWindowType.Post16, PupilSearchField.CypmdId));
        Assert.False(PupilSuggestionFormat.Matches(
            Post16(cypmdId: "500001"), "Bil", CheckingWindowType.Post16, PupilSearchField.CypmdId));
    }

    // ── Labels — PupilSearchField.CypmdId (AB#304118) ──────────────────────────
    // Why the ID has to be in the text: when a clerk searches by ID and two duplicate records were
    // created from the same child they get rows that read alike. The ID is what they were typing, so
    // it is what has to come back. Since #623 every KS4 suggestion carries it, so a page narrowed to
    // the ID shows the same label as every other KS4 page.

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void A_cypmd_id_search_shows_the_same_label_as_any_ks4_search(CheckingWindowType windowType)
    {
        Assert.Equal(Ks4Label, PupilSuggestionFormat.Label(
            CypmdIdPupil, windowType, PupilSearchField.CypmdId));
    }

    [Fact]
    public void A_16_19_suggestion_is_unchanged_even_when_it_is_narrowed_to_the_id()
    {
        // 16-19 already prints "CYPMD ID:..." — a second, differently-bracketed copy would be noise,
        // and #510 does not cover that journey.
        Assert.Equal("Billy, B, (CYPMD ID:500001, ULN:9900000001, DOB:12/03/2007, INCLUDED)",
            PupilSuggestionFormat.Label(
                Post16(), CheckingWindowType.Post16, PupilSearchField.CypmdId));
    }

    [Fact]
    public void The_id_is_shown_on_a_ks4_pupil_whose_id_is_only_the_thing_that_identifies_them()
    {
        // The motivating case: two records off one child. Identical name and DOB, so the name part
        // of the label is no help at all.
        var original = Ks4(firstname: "Jane", surname: "Smith", dob: "01/01/2010", cypmdId: "800001");
        var duplicate = Ks4(firstname: "Jane", surname: "Smith", dob: "01/01/2010", cypmdId: "800002");

        var labels = new[] { original, duplicate }
            .Select(p => PupilSuggestionFormat.Label(p, CheckingWindowType.KS4June, PupilSearchField.CypmdId))
            .ToList();

        Assert.Equal(2, labels.Distinct().Count());
    }

    // ── US3 — the pages this ticket does not touch (FR-008, FR-010) ────────────
    // Restated with the field value spelled out rather than left to the default. The default is
    // already covered above; passing All explicitly is what pins the value itself, so the CypmdId
    // branch cannot be reached by accident on a page that never asked for it.

    [Theory]
    [InlineData("Jane")]            // forename
    [InlineData("Smith")]           // surname
    [InlineData("Jane Smith")]      // split query
    [InlineData("A8604070001B")]    // UPN
    [InlineData("800001")]          // CYPMD ID — still matchable where the page allows it
    [InlineData("01/01/2010")]      // date of birth (#623)
    [InlineData("01/01")]           // a partial date of birth
    public void An_unnarrowed_ks4_search_matches_name_upn_cypmd_id_and_date_of_birth(string query)
    {
        Assert.True(PupilSuggestionFormat.Matches(
            CypmdIdPupil, query, CheckingWindowType.KS4June, PupilSearchField.All));
    }

    [Theory]
    [InlineData(PupilSearchField.All)]
    [InlineData(PupilSearchField.CypmdId)]
    public void A_16_19_suggestion_is_byte_identical_under_both_field_values(PupilSearchField searchField)
    {
        // 16-19 already prints the ID, so neither value may alter its string.
        Assert.Equal("Billy, B, (CYPMD ID:500001, ULN:9900000001, DOB:12/03/2007, INCLUDED)",
            PupilSuggestionFormat.Label(Post16(), CheckingWindowType.Post16, searchField));
    }

    [Fact]
    public void Omitting_the_field_is_the_same_as_asking_for_everything()
    {
        // The default is what every existing caller relies on — the Add journey's duplicate check
        // reaches these helpers without naming a field. Compared rather than hard-coded so the
        // assertion tracks the default instead of restating it.
        const CheckingWindowType windowType = CheckingWindowType.KS4June;
        foreach (var query in new[] { "Smith", "Jane", "A8604070001B", "800001", "01/01/2010", "" })
        {
            Assert.Equal(
                PupilSuggestionFormat.Matches(CypmdIdPupil, query, windowType, PupilSearchField.All),
                PupilSuggestionFormat.Matches(CypmdIdPupil, query, windowType));
            Assert.Equal(
                PupilSuggestionFormat.Label(CypmdIdPupil, windowType, PupilSearchField.All),
                PupilSuggestionFormat.Label(CypmdIdPupil, windowType));
        }
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

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void A_ks4_pupil_is_found_by_date_of_birth_too(CheckingWindowType windowType)
    {
        // #623: the hint promises date of birth on every key stage, so every key stage searches it.
        var pupil = Ks4(dob: "2010-01-01");

        Assert.True(PupilSuggestionFormat.Matches(pupil, "01/01/2010", windowType));
        Assert.True(PupilSuggestionFormat.Matches(pupil, "01/01", windowType));
        Assert.False(PupilSuggestionFormat.Matches(pupil, "2010-01-01", windowType));
    }

    [Fact]
    public void A_ks4_pupil_is_still_found_by_name_upn_and_cypmd_id()
    {
        var pupil = Ks4(dob: "01/01/2010");

        Assert.True(PupilSuggestionFormat.Matches(pupil, "Smi", CheckingWindowType.KS4June));
        Assert.True(PupilSuggestionFormat.Matches(pupil, "A8604070001B", CheckingWindowType.KS4June));
        Assert.True(PupilSuggestionFormat.Matches(pupil, "000001", CheckingWindowType.KS4June));
    }

    [Fact]
    public void A_pupil_with_no_date_of_birth_does_not_throw()
        => Assert.False(PupilSuggestionFormat.Matches(Post16(dob: string.Empty), "12/03", CheckingWindowType.Post16));

    // ── Full-name search (every word starts a word in some field) ─────────────

    [Theory]
    [InlineData("John Smith")]
    [InlineData("Smith John")]       // either order
    [InlineData("Smith, John")]      // as the KS4 label shows it
    [InlineData("john sm")]          // partial, any case
    [InlineData("  John   Smith ")]  // spare whitespace
    [InlineData("Smith 01/01")]      // a name and a date of birth together
    public void A_full_name_search_finds_the_pupil(string query)
        => Assert.True(PupilSuggestionFormat.Matches(
            Ks4(firstname: "John", surname: "Smith"), query, CheckingWindowType.KS4June));

    [Theory]
    [InlineData("John Jones")]       // surname does not match
    [InlineData("Jane Smith")]       // first name does not match
    [InlineData("ohn")]              // inside a word, not at its start
    public void A_full_name_search_needs_every_word_to_start_a_field_word(string query)
        => Assert.False(PupilSuggestionFormat.Matches(
            Ks4(firstname: "John", surname: "Smith"), query, CheckingWindowType.KS4June));

    [Theory]
    [InlineData("Berg")]
    [InlineData("Mary Jane Van Der Berg")]
    [InlineData("Jane Berg")]
    public void A_multi_word_name_is_found_by_any_of_its_words(string query)
        => Assert.True(PupilSuggestionFormat.Matches(
            Ks4(firstname: "Mary Jane", surname: "Van Der Berg"), query, CheckingWindowType.KS4June));

    [Theory]
    [InlineData("A860", true)]          // UPN prefix, on the unnarrowed search field
    [InlineData("a860", true)]
    [InlineData("8604", false)]         // inside the UPN
    [InlineData("0000", true)]          // CYPMD ID prefix
    [InlineData("0001", false)]         // inside the CYPMD ID
    [InlineData("01/01/2010", true)]    // date of birth alone
    [InlineData("01/01", true)]         // partial date of birth alone
    [InlineData("2010", false)]         // inside the date of birth
    public void An_identifier_or_date_of_birth_alone_matches_from_its_start(string query, bool expected)
        => Assert.Equal(expected, PupilSuggestionFormat.Matches(
            Ks4(upn: "A8604070001B", cypmdId: "000001", dob: "01/01/2010"), query,
            CheckingWindowType.KS4June, PupilSearchField.All));

    [Theory]
    [InlineData("Jones")]
    [InlineData("Smith-Jones")]
    [InlineData("Ann Jones")]
    public void A_hyphenated_surname_is_found_by_either_part(string query)
        => Assert.True(PupilSuggestionFormat.Matches(
            Ks4(firstname: "Ann", surname: "Smith-Jones"), query, CheckingWindowType.KS4June));

    [Fact]
    public void A_post16_date_of_birth_alone_matches()
        => Assert.True(PupilSuggestionFormat.Matches(Post16(), "12/03/2007", CheckingWindowType.Post16));

    [Fact]
    public void The_parsed_matcher_gives_the_same_answer_as_matches()
    {
        var matches = PupilSuggestionFormat.Matcher("Smith Jo");

        Assert.True(matches(Ks4(firstname: "John", surname: "Smith")));
        Assert.False(matches(Ks4(firstname: "Jane", surname: "Smith")));
    }

    [Fact]
    public void A_full_name_search_works_the_same_on_a_16_19_window()
        => Assert.True(PupilSuggestionFormat.Matches(
            Post16(firstname: "Billy", surname: "Bremner"), "Bremner Billy", CheckingWindowType.Post16));

    // ── NameMatchesForDuplicateCheck (AB#297780) ─────────────────────────────

    [Fact]
    public void Duplicate_check_matcher_matches_on_substrings_not_just_prefixes()
    {
        // The duplicate check intentionally matches with Contains: a partial surname must
        // still surface the stored pupil ("mith" inside "Smith"), unlike the autocomplete.
        Assert.True(PupilSuggestionFormat.NameMatchesForDuplicateCheck("Johnny", "Smithson", "john smith"));
    }

    [Fact]
    public void Duplicate_check_matcher_catches_a_multi_word_surname_split_from_the_first_space()
    {
        // Clerk grouped as First="John", Last="Van Der Berg" → split at the first space works.
        Assert.True(PupilSuggestionFormat.NameMatchesForDuplicateCheck("John", "Van der Berg", "John Van Der Berg"));
    }

    [Fact]
    public void Duplicate_check_matcher_catches_a_multi_word_surname_that_only_lines_up_on_the_last_space()
    {
        // Stored first name is itself multi-word, so the clerk's "John | Michael Smith" grouping
        // only matches when split at the LAST space: first="John Michael", surname="Smith".
        Assert.True(PupilSuggestionFormat.NameMatchesForDuplicateCheck("John Michael", "Smith", "John Michael Smith"));
    }

    [Fact]
    public void Duplicate_check_matcher_rejects_when_neither_split_lines_up_both_parts()
    {
        Assert.False(PupilSuggestionFormat.NameMatchesForDuplicateCheck("John", "Jones", "John Michael Smith"));
    }

    [Fact]
    public void Duplicate_check_matcher_rejects_even_when_one_part_only_substring_matches()
    {
        // "Smith" sits inside "Smithson", but the firstname part "John" does not line up → no match.
        Assert.False(PupilSuggestionFormat.NameMatchesForDuplicateCheck("Jane", "Smithson", "John Smith"));
    }

    [Fact]
    public void Duplicate_check_matcher_is_case_insensitive_across_both_halves()
    {
        Assert.True(PupilSuggestionFormat.NameMatchesForDuplicateCheck("john michael", "smith", "John Michael Smith"));
    }

    [Fact]
    public void Duplicate_check_matcher_single_term_matches_either_name_part()
    {
        Assert.True(PupilSuggestionFormat.NameMatchesForDuplicateCheck("John", "Smith", "john"));
        Assert.True(PupilSuggestionFormat.NameMatchesForDuplicateCheck("John", "Smith", "mith"));
    }

    [Fact]
    public void Duplicate_check_matcher_returns_false_for_empty_query()
    {
        Assert.False(PupilSuggestionFormat.NameMatchesForDuplicateCheck("John", "Smith", ""));
        Assert.False(PupilSuggestionFormat.NameMatchesForDuplicateCheck("John", "Smith", "   "));
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
