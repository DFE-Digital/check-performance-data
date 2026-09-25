using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#296648: the service works out from data it already holds whether the second late results
// file is still awaited. It is never told separately. Since the results slots became data, "awaited"
// means: the exercise has a slot for the second late file in use, and the live release has not read
// it. October: awaited. November (the file is in the release): not. February (the slot is retired):
// not, so the guidance stops.
public sealed class LateResultsAvailabilityTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    private static CheckingWindowDatasetDto Slot(string tag, bool retired = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = tag,
        SourceFile = tag,
        FeedsJourney = true,
        Retired = retired
    };

    private static CheckingExerciseDto Exercise(IEnumerable<CheckingWindowDatasetDto> slots, params CheckingWindowDatasetDto[] released)
    {
        var release = new CheckingExerciseReleaseDto
        {
            Id = Guid.NewGuid(),
            Files = [.. released.Select(d => new CheckingExerciseReleaseFileDto { DatasetId = d.Id, DatasetName = d.Name, SourceFile = d.SourceFile })]
        };
        return new CheckingExerciseDto
        {
            ExerciseType = CheckingExerciseType.ResultsEnquiry,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today,
            Datasets = [.. slots],
            Releases = [release],
            CurrentReleaseId = release.Id
        };
    }

    private static Task<bool> IsAwaiting(CheckingExerciseDto? exercise, CancellationToken ct = default)
    {
        var resolver = Substitute.For<ICheckingExerciseStorageResolver>();
        resolver.ResolveAsync(WindowId, CheckingExerciseType.ResultsEnquiry, Arg.Any<CancellationToken>())
            .Returns(exercise);
        return new LateResultsAvailability(resolver).IsAwaitingSecondLateResultsAsync(WindowId, ct);
    }

    [Fact]
    public async Task October_the_second_late_file_is_awaited()
    {
        var inc = Slot(ResultsFileTags.Post16Included);
        var lr1 = Slot(ResultsFileTags.Post16LateResults1);
        var lr2 = Slot(ResultsFileTags.Post16LateResults2);

        Assert.True(await IsAwaiting(Exercise([inc, lr1, lr2], inc, lr1)));
    }

    [Fact]
    public async Task November_the_second_late_file_is_in_the_live_release()
    {
        var inc = Slot(ResultsFileTags.Post16Included);
        var lr2 = Slot(ResultsFileTags.Post16LateResults2);

        Assert.False(await IsAwaiting(Exercise([inc, lr2], inc, lr2)));
    }

    [Fact]
    public async Task February_the_second_late_slot_is_retired_so_the_guidance_stops()
    {
        var incRev = Slot(ResultsFileTags.Post16IncludedRevised);
        var lr2 = Slot(ResultsFileTags.Post16LateResults2, retired: true);

        Assert.False(await IsAwaiting(Exercise([incRev, lr2], incRev)));
    }

    [Fact]
    public async Task An_exercise_with_no_second_late_slot_awaits_nothing()
    {
        var inc = Slot(ResultsFileTags.Post16Included);

        Assert.False(await IsAwaiting(Exercise([inc], inc)));
    }

    [Fact]
    public async Task No_results_exercise_awaits_nothing()
        => Assert.False(await IsAwaiting(null));

    [Fact]
    public async Task The_cancellation_token_reaches_the_resolver()
    {
        var resolver = Substitute.For<ICheckingExerciseStorageResolver>();
        using var cts = new CancellationTokenSource();

        await new LateResultsAvailability(resolver).IsAwaitingSecondLateResultsAsync(WindowId, cts.Token);

        await resolver.Received(1).ResolveAsync(WindowId, CheckingExerciseType.ResultsEnquiry, cts.Token);
    }
}
