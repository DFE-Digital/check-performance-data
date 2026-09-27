using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#296648: the late results guidance shows while the admin says so on the window's results
// enquiry exercise. It names no file, so it works however the late results arrive.
public sealed class LateResultsWarningTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    private static Task<bool> Shows(CheckingExerciseDto? exercise, CancellationToken ct = default)
    {
        var resolver = Substitute.For<ICheckingExerciseStorageResolver>();
        resolver.ResolveAsync(WindowId, CheckingExerciseType.ResultsEnquiry, Arg.Any<CancellationToken>())
            .Returns(exercise);
        return new LateResultsWarning(resolver).ShowAsync(WindowId, ct);
    }

    private static CheckingExerciseDto Exercise(bool showWarning) => new()
    {
        ExerciseType = CheckingExerciseType.ResultsEnquiry,
        StartDate = DateTime.Today,
        EndDate = DateTime.Today,
        ShowLateResultsWarning = showWarning
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_warning_follows_the_admins_choice(bool showWarning)
        => Assert.Equal(showWarning, await Shows(Exercise(showWarning)));

    [Fact]
    public async Task No_results_exercise_shows_no_warning()
        => Assert.False(await Shows(null));

    [Fact]
    public async Task The_cancellation_token_reaches_the_resolver()
    {
        var resolver = Substitute.For<ICheckingExerciseStorageResolver>();
        using var cts = new CancellationTokenSource();

        await new LateResultsWarning(resolver).ShowAsync(WindowId, cts.Token);

        await resolver.Received(1).ResolveAsync(WindowId, CheckingExerciseType.ResultsEnquiry, cts.Token);
    }
}
