using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

/// <summary>
/// #318: the closed-exercise gate now runs on every journey entry point, so every test that drives
/// one needs a checking-exercise service. These build the two answers a controller test cares
/// about, so no test has to construct exercise rows and a TimeProvider just to stay open.
/// </summary>
public static class OpenCheckingExercises
{
    /// <summary>A service that reports every checking exercise open.</summary>
    public static ICheckingExerciseService AlwaysOpen()
    {
        var service = Substitute.For<ICheckingExerciseService>();
        service.IsOpen(default!, default, default).ReturnsForAnyArgs(true);
        service.IsOpen(default(CheckingExerciseDto)!).ReturnsForAnyArgs(true);
        return service.WithRealEndDates();
    }

    /// <summary>Re-stubs an existing service so every checking exercise now reports closed.</summary>
    public static ICheckingExerciseService Close(this ICheckingExerciseService service)
    {
        service.IsOpen(default!, default, default).ReturnsForAnyArgs(false);
        service.IsOpen(default(CheckingExerciseDto)!).ReturnsForAnyArgs(false);
        return service;
    }

    /// <summary>
    /// AB#307773: re-stubs an existing service so every checking exercise reports open only to the
    /// school with <paramref name="laestab"/>, as an open extension naming it would once the
    /// exercise's own end has passed. Compared as given, so a caller passing anything but the
    /// signed-in school's claim is told the exercise is closed.
    /// </summary>
    public static ICheckingExerciseService OpenOnlyFor(this ICheckingExerciseService service, string laestab)
    {
        service.Close();
        service.IsOpen(default!, default, default).ReturnsForAnyArgs(ci => ci.ArgAt<string?>(2) == laestab);
        return service;
    }

    /// <summary>A service that reports every checking exercise closed.</summary>
    public static ICheckingExerciseService AlwaysClosed()
    {
        var service = Substitute.For<ICheckingExerciseService>();
        service.IsOpen(default!, default, default).ReturnsForAnyArgs(false);
        service.IsOpen(default(CheckingExerciseDto)!).ReturnsForAnyArgs(false);
        return service.WithRealEndDates();
    }

    /// <summary>A service that reports only <paramref name="open"/> open.</summary>
    public static ICheckingExerciseService Only(CheckingExerciseType open)
    {
        var service = Substitute.For<ICheckingExerciseService>();
        service.IsOpen(default!, default, default)
            .ReturnsForAnyArgs(ci => ci.ArgAt<CheckingExerciseType>(1) == open);
        service.IsOpen(default(CheckingExerciseDto)!)
            .ReturnsForAnyArgs(ci => ci.ArgAt<CheckingExerciseDto>(0).ExerciseType == open);
        return service.WithRealEndDates();
    }

    /// <summary>
    /// Answers EndDateFor from the rows it is given, exactly as the real service does. Only IsOpen
    /// is faked here — a substitute left to return null would make every deadline sentence vanish
    /// and hide the bug #320 fixed, which is that the wrong date was being read. The school's
    /// end applies (AB#307773), as the row's own EndDateFor does.
    /// </summary>
    private static ICheckingExerciseService WithRealEndDates(this ICheckingExerciseService service)
    {
        service.EndDateFor(default!, default, default).ReturnsForAnyArgs(ci =>
            ci.ArgAt<IReadOnlyList<CheckingExerciseDto>>(0)
                .FirstOrDefault(e => e.ExerciseType == ci.ArgAt<CheckingExerciseType>(1))
                ?.EndDateFor(ci.ArgAt<string?>(2)));
        return service;
    }
}
