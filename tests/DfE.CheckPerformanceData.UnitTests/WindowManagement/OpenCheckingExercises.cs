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
        service.IsOpen(default!, default).ReturnsForAnyArgs(true);
        return service;
    }

    /// <summary>Re-stubs an existing service so every checking exercise now reports closed.</summary>
    public static ICheckingExerciseService Close(this ICheckingExerciseService service)
    {
        service.IsOpen(default!, default).ReturnsForAnyArgs(false);
        return service;
    }

    /// <summary>A service that reports every checking exercise closed.</summary>
    public static ICheckingExerciseService AlwaysClosed()
    {
        var service = Substitute.For<ICheckingExerciseService>();
        service.IsOpen(default!, default).ReturnsForAnyArgs(false);
        return service;
    }

    /// <summary>A service that reports only <paramref name="open"/> open.</summary>
    public static ICheckingExerciseService Only(CheckingExerciseType open)
    {
        var service = Substitute.For<ICheckingExerciseService>();
        service.IsOpen(default!, default)
            .ReturnsForAnyArgs(ci => ci.ArgAt<CheckingExerciseType>(1) == open);
        return service;
    }
}
