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

    // ── AB#307773: extensions ────────────────────────────────────────────────

    [Fact]
    public async Task Each_extension_answers_for_itself_and_offers_only_what_its_state_allows()
    {
        // Edit and Close early while open, Send requests once closed — asked of the one clock for
        // each extension, whatever the exercise's own state. Listed by end date.
        var row = Row(CheckingExerciseType.PupilData, LastMonth, Yesterday);
        var open = new CheckingExerciseExtensionDto { Id = Guid.NewGuid(), EndDate = NextMonth, Laestabs = ["9334290", "8604070"] };
        var closed = new CheckingExerciseExtensionDto
        {
            Id = Guid.NewGuid(), EndDate = Yesterday.AddHours(1), Laestabs = ["1234567"],
            RequestsSentAt = new DateTime(2026, 9, 2, 20, 0, 0)
        };
        row.Extensions.AddRange([open, closed]);
        TheWindowHolds(row);

        var section = await SectionAsync(row.Id);

        Assert.Equal([closed.Id, open.Id], section.Extensions.Select(x => x.Id));
        var openRow = section.Extensions[1];
        Assert.True(openRow.IsOpen);
        Assert.False(openRow.HasClosed);
        Assert.False(openRow.WaitsForExercise);
        Assert.False(openRow.RequestsSent);
        Assert.Equal(2, openRow.SchoolCount);
        Assert.Equal(NextMonth, openRow.EndDate);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{row.Id}/extensions/{open.Id}/edit", openRow.EditLink);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{row.Id}/extensions/{open.Id}/close", openRow.CloseLink);
        var closedRow = section.Extensions[0];
        Assert.False(closedRow.IsOpen);
        Assert.True(closedRow.HasClosed);
        Assert.False(closedRow.WaitsForExercise);
        Assert.True(closedRow.RequestsSent);
        Assert.Equal(1, closedRow.SchoolCount);
        Assert.Equal(
            $"/admin/windows/{WindowId}/exercises/{row.Id}/extensions/{closed.Id}/send-requests",
            closedRow.SendRequestsLink);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{row.Id}/extensions/new", section.AddExtensionLink);
    }

    [Fact]
    public async Task An_extension_closed_early_while_its_exercise_runs_waits_for_the_exercise()
    {
        // AB#307773 review C1, the three card states. Own end ahead: Open (Edit, Close early).
        // Own end passed but the exercise still runs: Closed, no links, "Ends with the exercise on
        // …". Both passed: Closed and Send requests.
        var row = Row(CheckingExerciseType.PupilData, LastMonth, NextMonth);
        var open = new CheckingExerciseExtensionDto { Id = Guid.NewGuid(), EndDate = NextYear, Laestabs = ["9334290"] };
        var closedEarly = new CheckingExerciseExtensionDto { Id = Guid.NewGuid(), EndDate = Yesterday, Laestabs = ["1234567"] };
        row.Extensions.AddRange([open, closedEarly]);
        var ended = Row(CheckingExerciseType.ResultsEnquiry, LastMonth, Yesterday);
        var closed = new CheckingExerciseExtensionDto { Id = Guid.NewGuid(), EndDate = Yesterday.AddHours(1), Laestabs = ["8604070"] };
        ended.Extensions.Add(closed);
        TheWindowHolds(row, ended);

        var waiting = Assert.Single((await SectionAsync(row.Id)).Extensions, x => x.Id == closedEarly.Id);
        Assert.False(waiting.IsOpen);
        Assert.False(waiting.HasClosed);
        Assert.True(waiting.WaitsForExercise);
        Assert.Equal("01/10/2026 00:00", waiting.ExerciseEndDateText);

        var stillOpen = Assert.Single((await SectionAsync(row.Id)).Extensions, x => x.Id == open.Id);
        Assert.True(stillOpen.IsOpen);
        Assert.False(stillOpen.WaitsForExercise);

        var sendable = Assert.Single((await SectionAsync(ended.Id)).Extensions);
        Assert.False(sendable.IsOpen);
        Assert.True(sendable.HasClosed);
        Assert.False(sendable.WaitsForExercise);
    }

    [Fact]
    public async Task An_exercise_with_no_extensions_may_still_have_one_added()
    {
        // Add is allowed at any time, before the exercise ends or after (decision 3).
        var row = Row(CheckingExerciseType.ResultsEnquiry, LastMonth, Yesterday);
        TheWindowHolds(row);

        var section = await SectionAsync(row.Id);

        Assert.Empty(section.Extensions);
        Assert.Equal($"/admin/windows/{WindowId}/exercises/{row.Id}/extensions/new", section.AddExtensionLink);
    }

    [Fact]
    public async Task A_data_share_has_no_extensions_and_no_add_link()
    {
        // No kind, so no journey and no time to extend; the view also guards on the kind (E2E).
        var share = new CheckingExerciseDto
        {
            Id = Guid.NewGuid(), ExerciseType = null, DisplayOnly = true,
            StartDate = LastMonth, EndDate = NextYear
        };
        TheWindowHolds(share);

        var section = await SectionAsync(share.Id);

        Assert.Empty(section.Extensions);
        Assert.Null(section.AddExtensionLink);
    }
}
