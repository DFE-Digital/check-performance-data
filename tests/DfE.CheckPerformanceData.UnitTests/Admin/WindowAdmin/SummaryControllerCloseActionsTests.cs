using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// AB#301022: the window details page says, per exercise, whether it is open — and so which of
// Close and "Send requests for processing" it may offer. The answer comes from the one clock
// (the real CheckingExerciseService on a fixed TimeProvider), never from the controller. Each
// section answers for its own row, not for its kind: a window may hold several releases of one
// kind (#466).
public sealed class SummaryControllerCloseActionsTests
{
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTime LastMonth = new(2026, 8, 1);
    private static readonly DateTime Yesterday = new(2026, 9, 2, 17, 0, 0);
    private static readonly DateTime NextMonth = new(2026, 10, 1);
    private static readonly DateTime NextYear = new(2027, 8, 2, 17, 0, 0);

    private readonly IWindowService _windowService = Substitute.For<IWindowService>();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private SummaryController Sut() =>
        new(_windowService, new CheckingExerciseService(new FixedTimeProvider(Now)));

    private void TheWindowHolds(params CheckingExerciseDto[] exercises) =>
        _windowService.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(new CheckingWindowDto
        {
            Id = WindowId,
            Title = "Key Stage 4 June",
            KeyStage = KeyStages.KS4,
            CheckingWindowType = CheckingWindowType.KS4June,
            StartDate = exercises.Min(e => e.StartDate),
            EndDate = exercises.Max(e => e.EndDate),
            Exercises = [.. exercises]
        });

    private static CheckingExerciseDto Row(CheckingExerciseType type, DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid(),
        ExerciseType = type,
        StartDate = start,
        EndDate = end,
        TabOrder = (int)type
    };

    private async Task<IReadOnlyList<ExerciseSummarySection>> SectionsAsync()
    {
        var view = Assert.IsType<ViewResult>(await Sut().Index(WindowId, CancellationToken.None));
        return Assert.IsType<WindowEditItem>(view.Model).Exercises;
    }

    private async Task<ExerciseSummarySection> SectionAsync(Guid exerciseId) =>
        (await SectionsAsync()).Single(e => e.Id == exerciseId);

    [Fact]
    public async Task An_open_exercise_is_open_and_may_be_closed()
    {
        // AC1.
        var row = Row(CheckingExerciseType.PupilData, LastMonth, NextYear);
        TheWindowHolds(row);

        var section = await SectionAsync(row.Id);

        Assert.True(section.IsOpen);
        Assert.False(section.HasClosed);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{row.Id}/close", section.CloseLink);
    }

    [Fact]
    public async Task An_ended_exercise_is_closed_and_may_only_have_its_requests_sent()
    {
        // AC2 — and each exercise answers for itself: results enquiry is still open beside it.
        var ended = Row(CheckingExerciseType.PupilData, LastMonth, Yesterday);
        var running = Row(CheckingExerciseType.ResultsEnquiry, LastMonth, NextYear);
        TheWindowHolds(ended, running);

        var closed = await SectionAsync(ended.Id);
        var open = await SectionAsync(running.Id);

        Assert.False(closed.IsOpen);
        Assert.True(closed.HasClosed);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{ended.Id}/send-requests", closed.SendRequestsLink);
        Assert.True(open.IsOpen);
        Assert.False(open.HasClosed);
    }

    [Fact]
    public async Task An_exercise_that_has_not_started_offers_neither()
    {
        // Not open is not the same as closed: there is nothing to close and nothing to send yet.
        var row = Row(CheckingExerciseType.PupilData, NextMonth, NextYear);
        TheWindowHolds(row);

        var section = await SectionAsync(row.Id);

        Assert.False(section.IsOpen);
        Assert.False(section.HasClosed);
    }

    [Fact]
    public async Task Two_releases_of_one_kind_each_answer_for_themselves()
    {
        // #466: an autumn release that has ended and a revised release still open. Asked by kind,
        // both would read open and closed at once, and the page would offer both buttons on both.
        var autumn = Row(CheckingExerciseType.PupilData, LastMonth, Yesterday);
        var revised = Row(CheckingExerciseType.PupilData, LastMonth, NextYear);
        TheWindowHolds(autumn, revised);

        var autumnSection = await SectionAsync(autumn.Id);
        var revisedSection = await SectionAsync(revised.Id);

        Assert.False(autumnSection.IsOpen);
        Assert.True(autumnSection.HasClosed);
        Assert.True(revisedSection.IsOpen);
        Assert.False(revisedSection.HasClosed);
    }
}
