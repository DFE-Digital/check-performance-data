using DfE.CheckPerformanceData.Application.Egress;
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
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 6, 8, 13, 35, 0, TimeSpan.Zero));

    private EgressPreprocessor Sut() => new(_repo, _clock, Substitute.For<ILogger<EgressPreprocessor>>());

    private static EgressSourceRecord Remove(string reference, string decision, string reason = "pupil-died") => new()
    {
        ChangeRequestId = Guid.NewGuid(), ReferenceNumber = reference, TicketId = reference.GetHashCode() & 0xffff, Decision = decision,
        OutputType = EgressOutputType.RemoveLearners, WindowType = CheckingWindowType.KS4June,
        SubmittedAtUtc = new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 142313, OrganisationLaestab = "860/4070",
        PupilFirstname = "Alice", PupilSurname = "Smith", PupilSex = "F", PupilDateOfBirth = "07/09/2010", PupilIdentifier = "A860407000011",
        PupilMatchRef = 555, PupilLaestab = "8604070", JourneyFound = true, Answers = new Dictionary<string, string> { ["reason"] = reason }
    };

    private void RunIs(EgressRunStatus status, params EgressSourceRecord[] records) =>
        _repo.GetRunAsync(RunId, Arg.Any<CancellationToken>()).Returns(new EgressRunDto(RunId, WindowId, status, Guid.NewGuid(), "Ops One",
            DateTime.UtcNow, null, null, null, null, [], null,
            [new EgressRunOutputDto(Guid.NewGuid(), EgressOutputType.RemoveLearners, true, records, records.Length, null, null, null)]));

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
        await _repo.Received(1).SavePreprocessedAsync(RunId, Arg.Is<IReadOnlyList<NewLearnerRow>>(l => l.Count == 0),
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
        await _repo.DidNotReceiveWithAnyArgs().SavePreprocessedAsync(default, default!, default!, default, default!, default);
        await _repo.Received(1).MarkPreprocessingFailedAsync(RunId,
            Arg.Is<IReadOnlyList<EgressRecordFailure>>(f => f.Count == 1 && f[0].ReferenceNumber == "R2" && f[0].Field == "Correction_Reason"),
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

        await _repo.Received(1).SavePreprocessedAsync(RunId, Arg.Any<IReadOnlyList<NewLearnerRow>>(), Arg.Any<IReadOnlyList<RemoveLearnerRow>>(),
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
    [Fact]
    public async Task A_preprocessing_failed_run_is_refused_not_re_run()
    {
        RunIs(EgressRunStatus.PreprocessingFailed, Remove("R1", "approved"));

        var events = await Collect();

        var only = Assert.Single(events);
        Assert.True(only.IsError);
        Assert.True(only.IsComplete);
        Assert.Contains("cannot be preprocessed", only.Message);
        await _repo.DidNotReceiveWithAnyArgs().TrySetStatusAsync(default, default, default, default);
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
        await _repo.DidNotReceiveWithAnyArgs().SavePreprocessedAsync(default, default!, default!, default, default!, default);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
    }
}
