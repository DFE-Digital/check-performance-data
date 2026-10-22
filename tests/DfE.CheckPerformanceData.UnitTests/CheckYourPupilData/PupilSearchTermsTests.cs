using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.UnitTests.CheckYourPupilData;

// Every pupil search box (journey autocomplete, pupil data tabs, schema-driven exercise tabs) uses
// this one rule: each typed word must match the start of a word in at least one field.
public sealed class PupilSearchTermsTests
{
    private static bool Matches(string query, params string?[] fields)
        => PupilSearchTerms.MatchesEveryWord(query, fields);

    [Theory]
    [InlineData("John Smith")]
    [InlineData("Smith John")]
    [InlineData("Smith, John")]
    [InlineData("Smith,John")]
    [InlineData("john smith")]
    [InlineData("JOHN SMITH")]
    [InlineData("Jo Sm")]
    [InlineData("  John   Smith  ")]
    public void A_full_name_finds_the_pupil_in_any_order(string query)
        => Assert.True(Matches(query, "John", "Smith"));

    [Theory]
    [InlineData("John")]
    [InlineData("Smith")]
    [InlineData("sm")]
    public void One_word_matches_either_name(string query)
        => Assert.True(Matches(query, "John", "Smith"));

    [Theory]
    [InlineData("ohn")]         // inside a word, not at its start
    [InlineData("mith")]
    [InlineData("John Jones")]  // one word matches, the other does not
    [InlineData("Jane Smith")]
    public void A_word_that_does_not_start_a_field_word_does_not_match(string query)
        => Assert.False(Matches(query, "John", "Smith"));

    [Theory]
    [InlineData("Berg")]
    [InlineData("Der")]
    [InlineData("Mary Berg")]
    [InlineData("Mary Van Der Berg")]
    [InlineData("Jane")]
    [InlineData("Mary Jane Berg")]
    [InlineData("Jones")]           // after a hyphen
    [InlineData("Smith-Jones")]
    public void A_word_may_match_the_start_of_any_word_in_a_multi_word_field(string query)
        => Assert.True(Matches(query, "Mary Jane", "Van Der Berg", "Smith-Jones"));

    [Theory]
    [InlineData("Brien")]           // after a straight apostrophe
    [InlineData("O'Brien")]
    [InlineData("Neill")]           // after a typographic apostrophe
    [InlineData("Ann")]             // after a tab in the field
    public void Apostrophes_and_tabs_in_a_field_start_a_new_word(string query)
        => Assert.True(Matches(query, "O'Brien", "O’Neill", "Mary\tAnn"));

    [Theory]
    [InlineData("A860", true)]          // the start of the UPN
    [InlineData("8604", false)]         // inside the UPN: no longer matches, by decision
    [InlineData("12/03", true)]         // the start of the date
    [InlineData("03/2010", false)]      // inside the date
    [InlineData("1234", true)]          // the start of a DfE number
    [InlineData("4567", false)]
    public void Identifiers_match_from_their_start_only(string query, bool expected)
        => Assert.Equal(expected, Matches(query, "A8604070001B", "12/03/2010", "1234567"));

    [Fact]
    public void A_typed_comma_never_matches_a_comma_in_a_field()
        // The comma separates words, so "Smith,J" is the words "Smith" and "J".
        => Assert.False(Matches("Smith,Jones", "Smith,Jones-Brown"));

    [Fact]
    public void A_parsed_query_can_be_reused_for_every_row()
    {
        var terms = PupilSearchTerms.Parse("John Smith");

        Assert.True(terms.Matches(["John", "Smith"]));
        Assert.False(terms.Matches(["Jane", "Smith"]));
        Assert.True(terms.Matches(["Johnny", "Smithson"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    public void A_query_with_no_words_is_empty(string? query)
        => Assert.True(PupilSearchTerms.Parse(query).IsEmpty);

    [Fact]
    public void Two_words_may_match_the_same_field()
        => Assert.True(Matches("Van Berg", "John", "Van Der Berg"));

    [Fact]
    public void Name_and_identifier_words_can_be_mixed()
        => Assert.True(Matches("Smith 12/03", "John", "Smith", "A8604070001B", "12/03/2010"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ")]
    public void Nothing_typed_matches_nothing(string query)
        => Assert.False(Matches(query, "John", "Smith"));

    [Fact]
    public void A_null_or_empty_field_is_skipped()
        => Assert.True(Matches("Smith", null, "", "Smith"));
}
