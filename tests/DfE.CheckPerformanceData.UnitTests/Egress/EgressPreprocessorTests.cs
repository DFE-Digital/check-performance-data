using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// The pipeline reports eight steps in a fixed order, keeps only approved records, and is
// all-or-nothing: one failing record means nothing is saved and every failure is listed.
public sealed class EgressPreprocessorTests
{
    private static readonly Guid RunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly IEgressRunRepository _repo = Substitute.For<IEgressRunRepository>();
    private readonly IWindowService _windows = Substitute.For<IWindowService>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 6, 8, 13, 35, 0, TimeSpan.Zero));

    private EgressPreprocessor Sut() => new(_repo, _windows, _clock, Substitute.For<ILogger<EgressPreprocessor>>());

    private static CheckingWindowDto Window(CheckingWindowType type = CheckingWindowType.KS4June) => new()
    {
        Id = WindowId, Title = "Window", KeyStage = KeyStages.KS4, CheckingWindowType = type,
        StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30)
    };

    private static EgressSourceRecord Remove(string reference, string decision, string reason = "pupil-died") => new()
    {
        ChangeRequestId = Guid.NewGuid(), ReferenceNumber = reference, TicketId = reference.GetHashCode() & 0xffff, Decision = decision,
        OutputType = EgressOutputType.RemoveLearners, WindowType = CheckingWindowType.KS4June,
        SubmittedAtUtc = new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 142313, OrganisationLaestab = "860/4070",
        PupilFirstname = "Alice", PupilSurname = "Smith", PupilSex = "F", PupilDateOfBirth = "07/09/2010", PupilIdentifier = "A860407000011",
        PupilMatchRef = 555, PupilLaestab = "8604070", JourneyFound = true, Answers = new Dictionary<string, string> { ["reason"] = reason }
    };

    private void RunIs(EgressRunStatus status, params EgressSourceRecord[] records)
    {
        _repo.GetRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressRunDto(RunId, WindowId, status, Guid.NewGuid(), "Ops One",
            DateTime.UtcNow, null, null, null, null, [], null,
            [new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, records, records.Length, null, null, null)]));
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window());
        // M4: the guarded writes now return rows affected — default to "won the race" (1) so
        // existing success/failure-path tests are unaffected; the specific lost-the-race test
        // overrides this explicitly.
        _repo.SavePreprocessedAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<IReadOnlyList<NewLearnerRow>>(),
            Arg.Any<IReadOnlyList<RemoveLearnerRow>>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyDictionary<EgressOutputType, string>>(), Arg.Any<CancellationToken>()).Returns(1);
        _repo.MarkPreprocessingFailedAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<IReadOnlyList<EgressRecordFailure>>(), Arg.Any<CancellationToken>()).Returns(1);
    }

    private async Task<List<EgressProgress>> Collect()
    {
        var events = new List<EgressProgress>();
        await foreach (var e in Sut().RunAsync(RunId, CancellationToken.None)) events.Add(e);
        return events;
    }

    [Fact]
    public async Task Reports_the_eight_steps_in_order_and_saves_only_approved_records()
    {
        RunIs(EgressRunStatus.Pulled, Remove("R1", "approved"), Remove("R2", "auto_approved"), Remove("R3", "rejected"), Remove("R4", "scrutiny"), Remove("R5", EgressDecisions.NoTicket));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, Arg.Any<CancellationToken>()).Returns(true);

        var events = await Collect();

        Assert.Equal(EgressPreprocessor.StepNames, events.Where(e => e.State == "done").Select(e => e.StepName).ToArray());
        Assert.Equal(8, events.Last().TotalSteps);
        var filter = events.Single(e => e.StepName == "Filter records" && e.State == "done");
        Assert.Equal(5, filter.RecordsIn);
        Assert.Equal(2, filter.RecordsOut);
        var last = events.Last();
        Assert.True(last.IsComplete);
        Assert.False(last.IsError);
        Assert.Equal(EgressRunStatus.Preprocessed, last.FinalStatus);
        await _repo.Received(1).SavePreprocessedAsync(RunId, EgressRunStatus.Preprocessing, Arg.Is<IReadOnlyList<NewLearnerRow>>(l => l.Count == 0),
            Arg.Is<IReadOnlyList<RemoveLearnerRow>>(l => l.Count == 2 && l.All(r => r.CorrectionReason == "4")),
            new DateOnly(2026, 6, 8),
            Arg.Is<IReadOnlyDictionary<EgressOutputType, string>>(d => d[EgressOutputType.RemoveLearners] == "CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task One_failing_record_fails_the_batch_and_saves_nothing()
    {
        RunIs(EgressRunStatus.Pulled, Remove("R1", "approved"), Remove("R2", "approved", reason: "other"));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, Arg.Any<CancellationToken>()).Returns(true);

        var events = await Collect();

        var last = events.Last();
        Assert.True(last.IsComplete);
        Assert.True(last.IsError);
        Assert.Equal(EgressRunStatus.PreprocessingFailed, last.FinalStatus);
        Assert.Equal(1, last.FailureCount);
        Assert.Equal("failed", events.Single(e => e.StepName == "Save to database" && e.IsComplete).State);
        await _repo.DidNotReceiveWithAnyArgs().SavePreprocessedAsync(default, default, default!, default!, default, default!, default);
        await _repo.Received(1).MarkPreprocessingFailedAsync(RunId, EgressRunStatus.Preprocessing,
            Arg.Is<IReadOnlyList<EgressRecordFailure>>(f => f.Count == 1 && f[0].ReferenceNumber == "R2" && f[0].Field == "Correction_Reason"),
            Arg.Any<CancellationToken>());
    }

    // Nit (folded into M3): the file-name stage must come from the window, not be guessed from
    // records — a run with zero pulled records (a window with no candidate requests at all) must
    // still name its file after the window's own stage, never a hard-coded KS4June default.
    [Fact]
    public async Task File_name_stage_comes_from_the_window_even_when_the_run_has_no_records()
    {
        RunIs(EgressRunStatus.Pulled);
        _windows.GetByIdAsync(WindowId, Arg.Any<CancellationToken>()).Returns(Window(CheckingWindowType.KS2));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, Arg.Any<CancellationToken>()).Returns(true);

        await Collect();

        await _repo.Received(1).SavePreprocessedAsync(RunId, EgressRunStatus.Preprocessing, Arg.Any<IReadOnlyList<NewLearnerRow>>(), Arg.Any<IReadOnlyList<RemoveLearnerRow>>(),
            Arg.Any<DateOnly>(), Arg.Is<IReadOnlyDictionary<EgressOutputType, string>>(d => d[EgressOutputType.RemoveLearners].Contains("_KS2_")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_date_is_the_London_calendar_date()
    {
        // 23:30 UTC on 8 June is 00:30 BST on 9 June — the file must say 9 June.
        _clock.SetUtcNow(new DateTimeOffset(2026, 6, 8, 23, 30, 0, TimeSpan.Zero));
        RunIs(EgressRunStatus.Pulled, Remove("R1", "approved"));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, Arg.Any<CancellationToken>()).Returns(true);

        await Collect();

        await _repo.Received(1).SavePreprocessedAsync(RunId, EgressRunStatus.Preprocessing, Arg.Any<IReadOnlyList<NewLearnerRow>>(), Arg.Any<IReadOnlyList<RemoveLearnerRow>>(),
            new DateOnly(2026, 6, 9), Arg.Any<IReadOnlyDictionary<EgressOutputType, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_run_that_is_not_in_a_runnable_state_ends_with_an_error_event_and_touches_nothing()
    {
        RunIs(EgressRunStatus.Transferred, Remove("R1", "approved"));

        var events = await Collect();

        var only = Assert.Single(events);
        Assert.True(only.IsError);
        Assert.True(only.IsComplete);
        await _repo.DidNotReceiveWithAnyArgs().TrySetStatusAsync(default, default, default, default);
    }

    // M2: re-running a PreprocessingFailed run bypassed the lock — the failed run releases its
    // pair (Failed.cshtml says "start a new run"), so a re-run reaching Preprocessed while a
    // colleague's fresh run also holds the pair would let both transfer it. Same terminal refusal
    // shape as any other non-runnable status.
    //
    // Second-pass nit: the refusal text used to interpolate the raw enum member name
    // ("PreprocessingFailed"), leaking C# to the end user. It must use the same human label
    // Index.cshtml's StageLabel helper shows for this status ("Preprocessing failed") instead.
    //
    // Final-review nit: interpolating that label straight into "This run is {label} and cannot
    // be preprocessed" reads as ungrammatical English for several labels (e.g. "This run is
    // Transfer failed and cannot be preprocessed."). Phrased as a stage statement instead, which
    // reads correctly for every label EgressRunStatuses.Label can return.
    [Fact]
    public async Task A_preprocessing_failed_run_is_refused_not_re_run()
    {
        RunIs(EgressRunStatus.PreprocessingFailed, Remove("R1", "approved"));

        var events = await Collect();

        var only = Assert.Single(events);
        Assert.True(only.IsError);
        Assert.True(only.IsComplete);
        Assert.Equal("This run cannot be preprocessed because its stage is Preprocessing failed.", only.Message);
        await _repo.DidNotReceiveWithAnyArgs().TrySetStatusAsync(default, default, default, default);
    }

    // M4: SavePreprocessedAsync's own guard can lose the race (an Abandon landed while this
    // pipeline ran) without throwing — 0 rows means the run must not be reported as Preprocessed.
    [Fact]
    public async Task Zero_rows_from_SavePreprocessed_reports_the_run_was_abandoned_not_preprocessed()
    {
        RunIs(EgressRunStatus.Pulled, Remove("R1", "approved"));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, Arg.Any<CancellationToken>()).Returns(true);
        _repo.SavePreprocessedAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<IReadOnlyList<NewLearnerRow>>(),
            Arg.Any<IReadOnlyList<RemoveLearnerRow>>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyDictionary<EgressOutputType, string>>(), Arg.Any<CancellationToken>()).Returns(0);

        var events = await Collect();

        var last = events.Last();
        Assert.True(last.IsComplete);
        Assert.True(last.IsError);
        Assert.Null(last.FinalStatus);
        Assert.Contains("abandoned", last.Message);
    }

    // M4: same race for the failure path — the run must not be reported PreprocessingFailed (with
    // its failures pinned) when it is actually Abandoned.
    [Fact]
    public async Task Zero_rows_from_MarkPreprocessingFailed_reports_the_run_was_abandoned_not_failed()
    {
        RunIs(EgressRunStatus.Pulled, Remove("R1", "approved"), Remove("R2", "approved", reason: "other"));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, Arg.Any<CancellationToken>()).Returns(true);
        _repo.MarkPreprocessingFailedAsync(Arg.Any<Guid>(), Arg.Any<EgressRunStatus>(), Arg.Any<IReadOnlyList<EgressRecordFailure>>(), Arg.Any<CancellationToken>()).Returns(0);

        var events = await Collect();

        var last = events.Last();
        Assert.True(last.IsComplete);
        Assert.True(last.IsError);
        Assert.Null(last.FinalStatus);
        Assert.Contains("abandoned", last.Message);
    }

    [Fact]
    public async Task Cancellation_mid_run_puts_the_run_back_to_its_previous_state()
    {
        RunIs(EgressRunStatus.Pulled, Remove("R1", "approved"));
        _repo.TrySetStatusAsync(RunId, EgressRunStatus.Pulled, EgressRunStatus.Preprocessing, Arg.Any<CancellationToken>()).Returns(true);
        using var cts = new CancellationTokenSource();

        var enumerator = Sut().RunAsync(RunId, cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync());   // step 1 running
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { while (await enumerator.MoveNextAsync()) { } });
        await enumerator.DisposeAsync();

        await _repo.Received(1).TrySetStatusAsync(RunId, EgressRunStatus.Preprocessing, EgressRunStatus.Pulled, CancellationToken.None);
        await _repo.DidNotReceiveWithAnyArgs().SavePreprocessedAsync(default, default, default!, default!, default, default!, default);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
    }
}
