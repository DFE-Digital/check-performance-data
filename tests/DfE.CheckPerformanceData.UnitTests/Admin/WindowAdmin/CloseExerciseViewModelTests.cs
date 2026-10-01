using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#301022: the close confirmation page's own wording decisions.
public sealed class CloseExerciseViewModelTests
{
    private static CloseExerciseViewModel Model(CheckingExerciseType exercise) => new()
    {
        WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        WindowTitle = "Key Stage 4 June",
        ExerciseType = exercise,
        ExerciseLabel = "Label",
        ScheduledEnd = new DateTime(2027, 8, 2, 17, 0, 0)
    };

    [Fact]
    public void The_scheduled_end_reads_as_a_date_and_a_time_whatever_the_server_culture()
    {
        var model = Model(CheckingExerciseType.PupilData);

        Assert.Equal("02/08/2027", model.ScheduledEndDate);
        Assert.Equal("17:00", model.ScheduledEndTime);
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, "Closing it now stops schools submitting further amendment requests.")]
    [InlineData(CheckingExerciseType.ResultsEnquiry, "Closing it now stops schools reporting further issues with their results.")]
    public void The_consequence_says_what_schools_can_no_longer_do(CheckingExerciseType exercise, string expected)
        => Assert.Equal(expected, Model(exercise).Consequence);

    [Fact]
    public void Every_checking_exercise_has_a_consequence()
    {
        // A new exercise type must be given its own sentence, not borrow another's — the same rule
        // ClosedExerciseGuard holds for the school-facing message.
        foreach (var exercise in Enum.GetValues<CheckingExerciseType>())
            Assert.False(string.IsNullOrWhiteSpace(Model(exercise).Consequence));
    }

    [Fact]
    public void The_links_are_the_close_route_and_the_summary()
    {
        var model = Model(CheckingExerciseType.ResultsEnquiry);

        Assert.Equal("/admin/windows/11111111-1111-1111-1111-111111111111/ResultsEnquiry/close", model.PostUrl);
        Assert.Equal("/admin/windows/summary/11111111-1111-1111-1111-111111111111", model.CancelLink);
    }
}
