using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.WindowManagement;

public sealed class CheckingExerciseStorageResolverTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0);
    private readonly IWindowRepository _windows = Substitute.For<IWindowRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now, TimeSpan.Zero));

    private CheckingExerciseStorageResolver Resolver() => new(_windows, _clock);

    private void Window(Guid windowId, params CheckingExerciseDto[] exercises) =>
        _windows.GetExercisesOfKindAsync(windowId, CheckingExerciseType.PupilData, Arg.Any<CancellationToken>())
            .Returns(exercises);

    private static CheckingExerciseDto Exercise(Guid id, string tabName = "Pupils",
        bool enabled = true, bool displayOnly = false, DateTime? until = null) => new()
        {
            Id = id, ExerciseType = CheckingExerciseType.PupilData,
            StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1),
            TabName = tabName, IsEnabled = enabled, DisplayOnly = displayOnly, VisibleUntil = until
        };

    [Fact]
    public async Task ResolvesTheOnlyVisibleExerciseOfThatType()
    {
        var windowId = Guid.NewGuid();
        var id = Guid.NewGuid();
        Window(windowId, Exercise(id));

        var resolved = await Resolver().ResolveAsync(windowId, CheckingExerciseType.PupilData);

        Assert.Equal(id, resolved!.Id);
    }

    [Fact]
    public async Task ADisabledExercise_IsNeverResolved()
    {
        // Every exercise has a tab name now, so none is exempt: disabled means no journey data.
        var windowId = Guid.NewGuid();
        Window(windowId, Exercise(Guid.NewGuid(), enabled: false));

        Assert.Null(await Resolver().ResolveAsync(windowId, CheckingExerciseType.PupilData));
    }

    [Fact]
    public async Task An_old_duplicate_pair_that_is_both_live_resolves_to_nothing()
    {
        // Saved before the one-per-kind rule. Guessing would serve one exercise's data under the
        // other's journey.
        var windowId = Guid.NewGuid();
        Window(windowId, Exercise(Guid.NewGuid()), Exercise(Guid.NewGuid()));

        Assert.Null(await Resolver().ResolveAsync(windowId, CheckingExerciseType.PupilData));
    }

    [Fact]
    public async Task AnExpiredRelease_IsIgnored()
    {
        var windowId = Guid.NewGuid();
        var live = Guid.NewGuid();
        Window(windowId, Exercise(Guid.NewGuid(), until: Now.AddDays(-1)), Exercise(live));

        var resolved = await Resolver().ResolveAsync(windowId, CheckingExerciseType.PupilData);

        Assert.Equal(live, resolved!.Id);
    }

    [Fact]
    public async Task AnUnknownWindow_ResolvesToNothing()
    {
        _windows.GetExercisesOfKindAsync(Arg.Any<Guid>(), Arg.Any<CheckingExerciseType>(), Arg.Any<CancellationToken>())
            .Returns([]);

        Assert.Null(await Resolver().ResolveAsync(Guid.NewGuid(), CheckingExerciseType.PupilData));
    }

    [Fact]
    public async Task One_request_asks_the_database_once_per_window_and_kind()
    {
        // A results page asks from the results client, the late results warning and the request.
        var windowId = Guid.NewGuid();
        Window(windowId, Exercise(Guid.NewGuid()));
        var resolver = Resolver();

        await resolver.ResolveAsync(windowId, CheckingExerciseType.PupilData);
        await resolver.ResolveAsync(windowId, CheckingExerciseType.PupilData);

        await _windows.Received(1).GetExercisesOfKindAsync(windowId, CheckingExerciseType.PupilData,
            Arg.Any<CancellationToken>());
        await _windows.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
