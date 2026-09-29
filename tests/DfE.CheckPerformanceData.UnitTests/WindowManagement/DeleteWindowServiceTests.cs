using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

public class DeleteWindowServiceTests
{
    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IWindowDeletionRepository _repository = Substitute.For<IWindowDeletionRepository>();
    private readonly IWindowBlobStorage _blobs = Substitute.For<IWindowBlobStorage>();

    private DeleteWindowService Build() => new(_repository, _blobs);

    [Fact]
    public async Task Preview_groups_every_request_status_into_what_the_admin_loses()
    {
        _repository.CountRequestsByStatusAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<RequestStatus, int>
            {
                [RequestStatus.InProgress] = 1,
                [RequestStatus.ReadyToSubmit] = 2,
                [RequestStatus.SubmittedUnCommitted] = 3,
                [RequestStatus.SubmittedCommitted] = 4,
                [RequestStatus.Withdrawn] = 5,
                [RequestStatus.NotSubmitted] = 6
            });
        _repository.CountEgressRunsAsync(WindowId, Arg.Any<CancellationToken>()).Returns(7);

        var preview = await Build().PreviewAsync(WindowId, CancellationToken.None);

        Assert.Equal(3, preview.Drafts);
        Assert.Equal(3, preview.SubmittedNotSent);
        Assert.Equal(4, preview.SentForProcessing);
        Assert.Equal(11, preview.WithdrawnOrCancelled);
        Assert.Equal(7, preview.EgressRuns);
        Assert.Equal(21, preview.TotalRequests);
    }

    public static TheoryData<RequestStatus> AllStatuses() => new(Enum.GetValues<RequestStatus>());

    // A new status that no bucket names would be deleted without the page counting it.
    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task Every_request_status_is_counted(RequestStatus status)
    {
        _repository.CountRequestsByStatusAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<RequestStatus, int> { [status] = 1 });

        var preview = await Build().PreviewAsync(WindowId, CancellationToken.None);

        Assert.Equal(1, preview.TotalRequests);
    }

    [Fact]
    public async Task Preview_of_a_window_with_no_requests_has_none()
    {
        _repository.CountRequestsByStatusAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<RequestStatus, int>());

        var preview = await Build().PreviewAsync(WindowId, CancellationToken.None);

        Assert.False(preview.HasRequests);
    }

    [Fact]
    public async Task Delete_removes_the_rows_and_then_the_container()
    {
        _repository.DeleteAsync(WindowId, Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Build().DeleteAsync(WindowId, CancellationToken.None));

        Received.InOrder(() =>
        {
            _repository.DeleteAsync(WindowId, Arg.Any<CancellationToken>());
            _blobs.DeleteWindowContainerAsync(WindowId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Delete_of_an_unknown_window_touches_no_container()
    {
        _repository.DeleteAsync(WindowId, Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await Build().DeleteAsync(WindowId, CancellationToken.None));

        await _blobs.DidNotReceiveWithAnyArgs().DeleteWindowContainerAsync(default, default);
    }

    [Fact]
    public async Task A_failed_row_delete_keeps_the_container()
    {
        _repository.DeleteAsync(WindowId, Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build().DeleteAsync(WindowId, CancellationToken.None));

        await _blobs.DidNotReceiveWithAnyArgs().DeleteWindowContainerAsync(default, default);
    }
}
