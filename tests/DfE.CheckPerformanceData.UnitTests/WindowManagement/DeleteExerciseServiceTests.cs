using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

public class DeleteExerciseServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ExerciseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IExerciseDeletionRepository _repository = Substitute.For<IExerciseDeletionRepository>();
    private readonly IWindowBlobStorage _blobs = Substitute.For<IWindowBlobStorage>();

    private DeleteExerciseService Build() => new(_repository, _blobs);

    [Fact]
    public async Task Preview_groups_every_request_status_into_what_the_admin_loses()
    {
        _repository.CountRequestsByStatusAsync(ExerciseId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<RequestStatus, int>
            {
                [RequestStatus.InProgress] = 1,
                [RequestStatus.ReadyToSubmit] = 2,
                [RequestStatus.SubmittedUnCommitted] = 3,
                [RequestStatus.SubmittedCommitted] = 4,
                [RequestStatus.Withdrawn] = 5,
                [RequestStatus.NotSubmitted] = 6
            });

        var preview = await Build().PreviewAsync(ExerciseId, CancellationToken.None);

        Assert.Equal(3, preview.Drafts);
        Assert.Equal(3, preview.SubmittedNotSent);
        Assert.Equal(4, preview.SentForProcessing);
        Assert.Equal(11, preview.WithdrawnOrCancelled);
        Assert.Equal(21, preview.TotalRequests);
    }

    public static TheoryData<RequestStatus> AllStatuses() => new(Enum.GetValues<RequestStatus>());

    // A new status that no bucket names would be deleted without the page counting it.
    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Every_request_status_is_counted(RequestStatus status)
    {
        _repository.CountRequestsByStatusAsync(ExerciseId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<RequestStatus, int> { [status] = 1 });

        var preview = await Build().PreviewAsync(ExerciseId, CancellationToken.None);

        Assert.Equal(1, preview.TotalRequests);
    }

    [Fact]
    public async Task Delete_removes_the_rows_and_then_the_exercise_blobs()
    {
        _repository.DeleteAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns(new DeletedExercise(UsesExerciseStorage: true));

        Assert.True(await Build().DeleteAsync(WindowId, ExerciseId, CancellationToken.None));

        Received.InOrder(() =>
        {
            _repository.DeleteAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>());
            _blobs.DeleteExerciseBlobsAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>());
        });
    }

    // An older exercise's blobs sit under kind-named prefixes that a newer exercise of the same
    // kind may read, so no blob is deleted.
    [Fact]
    public async Task Delete_of_an_exercise_on_the_old_storage_keeps_its_blobs()
    {
        _repository.DeleteAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns(new DeletedExercise(UsesExerciseStorage: false));

        Assert.True(await Build().DeleteAsync(WindowId, ExerciseId, CancellationToken.None));

        await _blobs.DidNotReceiveWithAnyArgs().DeleteExerciseBlobsAsync(default, default, default);
        await _blobs.DidNotReceiveWithAnyArgs().DeleteWindowContainerAsync(default, default);
    }

    [Fact]
    public async Task Delete_of_an_unknown_exercise_touches_no_blob()
    {
        _repository.DeleteAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns((DeletedExercise?)null);

        Assert.False(await Build().DeleteAsync(WindowId, ExerciseId, CancellationToken.None));

        await _blobs.DidNotReceiveWithAnyArgs().DeleteExerciseBlobsAsync(default, default, default);
    }

    [Fact]
    public async Task A_failed_row_delete_keeps_the_blobs()
    {
        _repository.DeleteAsync(WindowId, ExerciseId, Arg.Any<CancellationToken>())
            .Returns<DeletedExercise?>(_ => throw new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build().DeleteAsync(WindowId, ExerciseId, CancellationToken.None));

        await _blobs.DidNotReceiveWithAnyArgs().DeleteExerciseBlobsAsync(default, default, default);
    }

    // The two prefixes a delete sweeps must not reach any other exercise's blobs: blob prefixes
    // match as plain strings, so each must end in "/" after the id.
    [Fact]
    public void Owned_prefixes_are_scoped_to_the_exercise_id()
    {
        var prefixes = CheckingExerciseBlobPaths.OwnedPrefixes(ExerciseId);

        Assert.Equal([$"ingress/{ExerciseId}/", $"exercises/{ExerciseId}/"], prefixes);
        Assert.Contains(prefixes, p => CheckingExerciseBlobPaths.DataPrefix(ExerciseId).StartsWith(p));
        Assert.Contains(prefixes, p => CheckingExerciseBlobPaths.LogPrefix(ExerciseId).StartsWith(p));
        Assert.Contains(prefixes, p => CheckingExerciseBlobPaths.DefinitionFile(ExerciseId, Guid.NewGuid(), "a.csv").StartsWith(p));
    }
}
