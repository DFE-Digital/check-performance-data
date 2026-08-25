using DfE.CheckPerformanceData.Domain.Time;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The only place in the solution that compares a checking window's outer dates against the clock.
/// The outer pair is the union of the window's exercises, and it alone decides whether the window is
/// open — whether or not any exercise inside it is. See docs/16-19-window-model.md.
/// </summary>
/// <remarks>
/// The window-level twin of <see cref="ICheckingExerciseService"/>, and it keeps the same rule: time
/// comes from the injected <see cref="TimeProvider"/> and is never accepted from a caller, because
/// that is what stops two callers reading different clocks and disagreeing. Do not reintroduce a
/// window-level IsOpen flag — one that only some read paths populate reads as "closed" everywhere
/// else, which is exactly how the admin summary page came to print "Is Open: False" for every
/// window.
///
/// Comparison is against <see cref="UkTime"/> rather than GetLocalNow: StartDate/EndDate are
/// wall-clock UK deadlines chosen by an admin — "closes 17:00" means 17:00 in London — and the
/// deploy containers run UTC, so a host-zone clock is an hour out through BST.
///
/// Takes the window DTO rather than a date pair so no caller can hand over the two dates the wrong
/// way round. LandingPage has an unrelated class of the same name, so a file importing both
/// namespaces must alias one, as Persistence already does.
/// </remarks>
public interface IWindowStatusService
{
    /// <summary>True when the window's outer pair brackets now.</summary>
    bool IsOpen(CheckingWindowDto window);

    /// <summary>Every window open right now, input order preserved. Empty is a valid answer.</summary>
    IReadOnlyList<CheckingWindowDto> OpenWindows(IEnumerable<CheckingWindowDto> windows);
}

/// <inheritdoc />
public sealed class WindowStatusService(TimeProvider timeProvider) : IWindowStatusService
{
    public bool IsOpen(CheckingWindowDto window) => Brackets(window, UkTime.Now(timeProvider));

    // One clock read for the whole list. Reading it per window would let a scan that happens to
    // straddle a boundary answer for two windows against two different instants.
    public IReadOnlyList<CheckingWindowDto> OpenWindows(IEnumerable<CheckingWindowDto> windows)
    {
        DateTime now = UkTime.Now(timeProvider);
        return [.. windows.Where(w => Brackets(w, now))];
    }

    // Inclusive at both ends, matching how an exercise's own dates are compared.
    private static bool Brackets(CheckingWindowDto window, DateTime now) =>
        window.StartDate <= now && window.EndDate >= now;
}
