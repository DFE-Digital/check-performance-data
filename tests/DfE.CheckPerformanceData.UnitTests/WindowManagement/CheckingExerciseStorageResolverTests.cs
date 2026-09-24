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
        _windows.GetByIdAsync(windowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = windowId, Title = "t", StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1),
            KeyStage = KeyStages.KS4, CheckingWindowType = CheckingWindowType.KS4June,
            Exercises = [.. exercises]
        });

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
    public async Task PrefersTheInteractiveReleaseOverADisplayOnlyOne()
    {
        var windowId = Guid.NewGuid();
        var interactive = Guid.NewGuid();
        Window(windowId, Exercise(Guid.NewGuid(), displayOnly: true), Exercise(interactive));

        var resolved = await Resolver().ResolveAsync(windowId, CheckingExerciseType.PupilData);

        Assert.Equal(interactive, resolved!.Id);
    }

    [Fact]
    public async Task TwoEquallyGoodReleases_ResolveToNothing()
    {
        // Guessing here would silently serve one release's data under another release's journey.
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
        _windows.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((CheckingWindowDto?)null);

        Assert.Null(await Resolver().ResolveAsync(Guid.NewGuid(), CheckingExerciseType.PupilData));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
