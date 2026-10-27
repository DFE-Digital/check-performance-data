using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

public class ExerciseSearchLabelTests
{
    private static readonly LearnerNoun Pupil = LearnerNoun.For(CheckingWindowType.KS2);

    [Fact]
    public void Names_every_searchable_column_in_display_order()
        // The KS2 and KS4 June pupil schemas: last name, first name and UPN are searchable.
        => Assert.Equal("Search for a pupil by last name, first name or UPN",
            ExerciseSearchLabel.For(Pupil, ["Last name", "First name", "UPN"]));

    [Fact]
    public void Two_columns_are_joined_with_or()
        => Assert.Equal("Search for a student by last name or first name",
            ExerciseSearchLabel.For(LearnerNoun.For(CheckingWindowType.Post16), ["Last name", "First name"]));

    [Fact]
    public void One_column_is_named_alone()
        => Assert.Equal("Search for a pupil by subject", ExerciseSearchLabel.For(Pupil, ["Subject"]));

    [Theory]
    [InlineData("UPN", "UPN")]
    [InlineData("CYPMD ID", "CYPMD ID")]
    [InlineData("Date of birth", "date of birth")]
    public void A_label_starts_lower_case_unless_it_is_an_abbreviation(string label, string expected)
        => Assert.Equal($"Search for a pupil by {expected}", ExerciseSearchLabel.For(Pupil, [label]));

    [Fact]
    public void No_searchable_column_names_none()
        => Assert.Equal("Search for a pupil", ExerciseSearchLabel.For(Pupil, []));
}
