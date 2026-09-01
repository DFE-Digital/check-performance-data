using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// The learner noun has no default case, so a checking window type added without one would throw on
// a live school's request. These tests are what makes that safe: adding a member to
// CheckingWindowType fails here until it is given a noun.
public sealed class LearnerNounTests
{
    public static TheoryData<CheckingWindowType> AllWindowTypes()
    {
        var data = new TheoryData<CheckingWindowType>();
        foreach (var type in Enum.GetValues<CheckingWindowType>()) data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllWindowTypes))]
    public void Every_window_type_has_a_noun_in_all_four_forms(CheckingWindowType type)
    {
        var noun = LearnerNoun.For(type);

        Assert.False(string.IsNullOrWhiteSpace(noun.Singular));
        Assert.False(string.IsNullOrWhiteSpace(noun.Plural));
        Assert.False(string.IsNullOrWhiteSpace(noun.SingularCapitalised));
        Assert.False(string.IsNullOrWhiteSpace(noun.PluralCapitalised));
    }

    [Theory]
    [MemberData(nameof(AllWindowTypes))]
    public void The_capitalised_forms_are_the_same_word_as_the_lower_case_ones(CheckingWindowType type)
    {
        // Stored rather than computed, so this is the check that a table header cannot disagree
        // with the sentence beneath it.
        var noun = LearnerNoun.For(type);

        Assert.Equal(noun.Singular, noun.SingularCapitalised.ToLowerInvariant());
        Assert.Equal(noun.Plural, noun.PluralCapitalised.ToLowerInvariant());
    }

    [Fact]
    public void Post16_says_student()
    {
        Assert.Equal("student", LearnerNoun.For(CheckingWindowType.Post16).Singular);
        Assert.Equal("students", LearnerNoun.For(CheckingWindowType.Post16).Plural);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void Every_other_key_stage_says_pupil(CheckingWindowType type)
    {
        Assert.Equal("pupil", LearnerNoun.For(type).Singular);
        Assert.Equal("pupils", LearnerNoun.For(type).Plural);
    }

    [Fact]
    public void An_unmapped_window_type_fails_loudly_rather_than_defaulting_to_pupil()
    {
        // A 16-19-like window type silently inheriting "pupil" is the failure this exists to
        // prevent, so a new enum member must throw until it is given a noun.
        var unmapped = (CheckingWindowType)999;

        Assert.Throws<ArgumentOutOfRangeException>(() => LearnerNoun.For(unmapped));
    }
}
