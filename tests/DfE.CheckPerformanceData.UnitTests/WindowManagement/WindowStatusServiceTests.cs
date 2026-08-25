using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.UnitTests.WindowManagement;

/// <summary>
/// #295435: these cases moved off WindowServiceTests when the window-level IsOpen flag was deleted.
/// WindowService no longer reads a clock, so open/closed is asserted here instead.
/// </summary>
public class WindowStatusServiceTests
{
    private static readonly DateTimeOffset Winter = new(2026, 1, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsOpen_is_true_when_the_window_brackets_now()
    {
        CheckingWindowDto window = Window(
            Winter.DateTime.AddDays(-1), Winter.DateTime.AddDays(1));

        Assert.True(Sut(Winter).IsOpen(window));
    }

    [Theory]
    [InlineData(-10, -5)] // already ended
    [InlineData(5, 10)]   // not yet started
    public void IsOpen_is_false_when_now_is_outside_the_window(int startOffsetDays, int endOffsetDays)
    {
        CheckingWindowDto window = Window(
            Winter.DateTime.AddDays(startOffsetDays), Winter.DateTime.AddDays(endOffsetDays));

        Assert.False(Sut(Winter).IsOpen(window));
    }

    // Inclusive at both ends, matching how an exercise's own dates are compared.
    [Fact]
    public void IsOpen_is_true_on_the_first_and_last_instant()
    {
        DateTime now = Winter.DateTime;

        Assert.True(Sut(Winter).IsOpen(Window(now, now.AddDays(1))));
        Assert.True(Sut(Winter).IsOpen(Window(now.AddDays(-1), now)));
    }

    [Fact]
    public void OpenWindows_returns_only_the_open_ones_in_input_order()
    {
        CheckingWindowDto ended = Window(Winter.DateTime.AddDays(-10), Winter.DateTime.AddDays(-5));
        CheckingWindowDto open = Window(Winter.DateTime.AddDays(-1), Winter.DateTime.AddDays(1));
        CheckingWindowDto future = Window(Winter.DateTime.AddDays(5), Winter.DateTime.AddDays(10));

        IReadOnlyList<CheckingWindowDto> result = Sut(Winter).OpenWindows([ended, open, future]);

        Assert.Equal([open.Id], result.Select(w => w.Id));
    }

    [Fact]
    public void OpenWindows_of_an_empty_list_is_empty()
    {
        Assert.Empty(Sut(Winter).OpenWindows([]));
    }

    /// <summary>
    /// The reason this compares against UkTime rather than the UTC instant. A window closing at
    /// 17:00 on a BST day is still open at 16:30 UK time — 15:30 UTC — and shut at 17:30 UK time.
    /// Read against UTC, the second of these would wrongly still be open.
    /// </summary>
    [Fact]
    public void IsOpen_reads_the_deadline_as_UK_wall_clock_through_BST()
    {
        // 1 July 2026 is BST, so UK wall-clock is UTC+1.
        CheckingWindowDto window = Window(
            new DateTime(2026, 7, 1, 9, 0, 0), new DateTime(2026, 7, 1, 17, 0, 0));

        DateTimeOffset halfPastFourUk = new(2026, 7, 1, 15, 30, 0, TimeSpan.Zero);
        DateTimeOffset halfPastFiveUk = new(2026, 7, 1, 16, 30, 0, TimeSpan.Zero);

        Assert.True(Sut(halfPastFourUk).IsOpen(window));
        Assert.False(Sut(halfPastFiveUk).IsOpen(window));
    }

    private static WindowStatusService Sut(DateTimeOffset utcNow) => new(new StubTimeProvider(utcNow));

    private static CheckingWindowDto Window(DateTime startDate, DateTime endDate) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "Window",
            StartDate = startDate,
            EndDate = endDate,
            KeyStage = KeyStages.KS2,
            CheckingWindowType = CheckingWindowType.KS2
        };

    // LocalTimeZone is deliberately left at the host's zone: UkTime must not consult it, and a stub
    // that pinned it to UTC would hide a regression back to GetLocalNow().
    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
