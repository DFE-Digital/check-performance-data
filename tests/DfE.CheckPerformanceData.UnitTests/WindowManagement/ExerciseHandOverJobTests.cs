using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// AB#302158: one tick of the job. Whether an exercise is due and what a hand-over does are
// AutomaticExerciseHandOverTests'; these pin the tick's own rules — only the pod holding the lock
// sweeps, the lock always comes back, and the off switch is off.
public sealed class ExerciseHandOverJobTests
{
    private static readonly DueExercise PupilData =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Key Stage 4 June", Guid.Parse("44444444-4444-4444-4444-444444444444"), CheckingExerciseType.PupilData, new DateTime(2026, 11, 2, 17, 0, 0));

    private static readonly DueExercise Enquiry =
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "16 to 19", Guid.Parse("55555555-5555-5555-5555-555555555555"), CheckingExerciseType.ResultsEnquiry, new DateTime(2026, 11, 2, 16, 0, 0));

    private readonly IExerciseHandOverLock _lock = Substitute.For<IExerciseHandOverLock>();
    private readonly IAutomaticExerciseHandOver _handOver = Substitute.For<IAutomaticExerciseHandOver>();

    private ExerciseHandOverJob Job(ExerciseHandOverSettings? settings = null) =>
        Job(Options.Create(settings ?? new ExerciseHandOverSettings()));

    private ExerciseHandOverJob Job(IOptions<ExerciseHandOverSettings> options)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _lock);
        services.AddScoped(_ => _handOver);
        var provider = services.BuildServiceProvider();

        return new ExerciseHandOverJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<ExerciseHandOverJob>.Instance);
    }

    private void LockIs(bool free) => _lock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(free);

    private void DueAre(params DueExercise[] due) =>
        _handOver.FindDueAsync(Arg.Any<CancellationToken>()).Returns(due);

    [Fact]
    public async Task A_pod_that_does_not_get_the_lock_skips_the_tick()
    {
        LockIs(free: false);

        await Job().RunOnceAsync(CancellationToken.None);

        await _handOver.DidNotReceiveWithAnyArgs().FindDueAsync(default);
        await _handOver.DidNotReceiveWithAnyArgs().HandOverAsync(default!, default);
    }

    [Fact]
    public async Task The_pod_holding_the_lock_hands_over_every_due_exercise_then_releases_it()
    {
        LockIs(free: true);
        DueAre(PupilData, Enquiry);

        await Job().RunOnceAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            _lock.TryAcquireAsync(Arg.Any<CancellationToken>());
            _handOver.FindDueAsync(Arg.Any<CancellationToken>());
            _handOver.HandOverAsync(PupilData, CancellationToken.None);
            _handOver.HandOverAsync(Enquiry, CancellationToken.None);
            _lock.ReleaseAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_stop_request_lets_the_hand_over_in_progress_finish_and_starts_no_other()
    {
        LockIs(free: true);
        DueAre(PupilData, Enquiry);
        using var stopping = new CancellationTokenSource();
        // The host asks the job to stop while the first exercise is being handed over.
        _handOver.HandOverAsync(PupilData, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stopping.Cancel();
                return new AutomaticHandOverOutcome(PupilData.WindowId, PupilData.ExerciseId, PupilData.Exercise, 1, 0, Failed: false);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Job().RunOnceAsync(stopping.Token));

        // The one in hand was not given the stop token, so it could not be cut short...
        await _handOver.Received(1).HandOverAsync(PupilData, CancellationToken.None);
        // ...the next one never started...
        await _handOver.DidNotReceive().HandOverAsync(Enquiry, Arg.Any<CancellationToken>());
        // ...and the lock still came back.
        await _lock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settings_that_cannot_be_read_switch_the_job_off_instead_of_stopping_the_host()
    {
        LockIs(free: true);
        var options = Substitute.For<IOptions<ExerciseHandOverSettings>>();
        options.Value.Returns(_ => throw new InvalidOperationException("Failed to convert configuration value"));
        var job = Job(options);

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!;
        await job.StopAsync(CancellationToken.None);

        await _lock.DidNotReceiveWithAnyArgs().TryAcquireAsync(default);
    }

    [Fact]
    public async Task A_tick_that_throws_does_not_end_the_job()
    {
        LockIs(free: true);
        var secondTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _handOver.FindDueAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("database is down");
                }

                secondTick.TrySetResult();
                return Task.FromResult<IReadOnlyList<DueExercise>>([]);
            });
        var job = Job(new ExerciseHandOverSettings { PollInterval = TimeSpan.FromMilliseconds(10) });

        await job.StartAsync(CancellationToken.None);
        await secondTick.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_lock_is_released_even_when_the_tick_throws()
    {
        // A lock a pod forgot to release would stop every pod's hand-over until that pod's
        // connection was recycled.
        LockIs(free: true);
        _handOver.FindDueAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<DueExercise>>(new InvalidOperationException("database is down")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Job().RunOnceAsync(CancellationToken.None));

        await _lock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Switched_off_it_never_takes_the_lock()
    {
        var job = Job(new ExerciseHandOverSettings { Enabled = false });

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!;
        await job.StopAsync(CancellationToken.None);

        await _lock.DidNotReceiveWithAnyArgs().TryAcquireAsync(default);
    }
}
