using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// The session table's only reaper used to be an opportunistic purge inside the preview action,
// which made deletion contingent on somebody starting another import — so the rows that survived
// longest were exactly the ones nobody came back for. This job is what sweeps regardless.
public class ContentStagingSessionRetentionJobTests
{
    private readonly IContentStagingSessionStore _sessions = Substitute.For<IContentStagingSessionStore>();

    private ContentStagingSessionRetentionJob NewJob() =>
        new(Substitute.For<IServiceScopeFactory>(),
            NullLogger<ContentStagingSessionRetentionJob>.Instance);

    [Fact]
    public async Task RunOnce_PurgesExpiredSessions()
    {
        _sessions.PurgeExpiredAsync(Arg.Any<CancellationToken>()).Returns(3);

        await NewJob().RunOnceAsync(_sessions, CancellationToken.None);

        await _sessions.Received(1).PurgeExpiredAsync(Arg.Any<CancellationToken>());
    }

    // A tick with nothing to do is the normal case; it must not be an error or a surprise.
    [Fact]
    public async Task RunOnce_WithNothingExpired_IsAQuietNoOp()
    {
        _sessions.PurgeExpiredAsync(Arg.Any<CancellationToken>()).Returns(0);

        await NewJob().RunOnceAsync(_sessions, CancellationToken.None);

        await _sessions.Received(1).PurgeExpiredAsync(Arg.Any<CancellationToken>());
    }

    // The store decides what "expired" means, from the session lifetime the read path already
    // enforces — the job must not carry a second, separately-configurable window that could
    // disagree with it.
    [Fact]
    public async Task RunOnce_DoesNotImposeItsOwnRetentionWindow()
    {
        await NewJob().RunOnceAsync(_sessions, CancellationToken.None);

        await _sessions.Received(1).PurgeExpiredAsync(Arg.Any<CancellationToken>());
        await _sessions.DidNotReceiveWithAnyArgs().DeleteAsync(default);
    }
}
