using System.Text.Json;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Observability;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;
using RulesConsumer = DfE.CheckPerformanceData.RulesEngineWorker.Consumers.RulesConsumer;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesEngine;

/// <summary>
/// End-to-end coverage of the rules consumer: queue-shaped JSON →
/// real <see cref="RuleContextMapper"/> → real <see cref="RulesEngineImpl"/> →
/// the decision persisted on the matching <see cref="ChangeRequest"/>.
///
/// Rules and lookups come from the seed JSON shipped at
/// <c>src/DfE.CheckPerformanceData.RulesEngineWorker/seed/</c> — the same files
/// <c>BlobRulesProvider</c> loads in production. One happy-path scenario per
/// Stage 1 outcome guards against drift in routing or per-table evaluation, and
/// proves the consumer writes back both the decision status and the outcome key.
/// </summary>
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class RulesEngineEndToEndTests
{
    private static readonly RulesSnapshot Snapshot = LoadSeedSnapshot();

    private readonly PostgresFixture _fixture;

    public RulesEngineEndToEndTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<OutcomeScenario> Scenarios()
    {
        var data = new TheoryData<OutcomeScenario>();
        foreach (var s in BuildScenarios()) data.Add(s);
        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Pipeline_RoutesScenarioToExpectedDecision(OutcomeScenario scenario)
    {
        var reference = scenario.ReferenceNumber;

        await using (var seedContext = _fixture.CreateContext())
        {
            if (!await seedContext.CheckingWindows.AnyAsync(w => w.Id == WindowId))
            {
                seedContext.CheckingWindows.Add(NewCheckingWindow());
                await seedContext.SaveChangesAsync();
            }

            seedContext.ChangeRequests.RemoveRange(
                seedContext.ChangeRequests.Where(r => r.ReferenceNumber == reference));
            await seedContext.SaveChangesAsync();
            seedContext.ChangeRequests.Add(NewChangeRequest(reference));
            await seedContext.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var queueService = new PostgresQueueService(context);
            var rulesProvider = Substitute.For<IRulesProvider>();
            rulesProvider.Current.Returns(Snapshot);

            var sut = new RulesConsumer(
                queueService,
                rulesProvider,
                new RulesEngineImpl(),
                new RuleContextMapper(),
                context);

            await sut.ProcessMessageBodyAsync(scenario.MessageJson, CancellationToken.None);
        }

        await using var verifyContext = _fixture.CreateContext();
        var persisted = await verifyContext.ChangeRequests
            .AsNoTracking()
            .SingleAsync(r => r.ReferenceNumber == reference);

        Assert.Equal(scenario.ExpectedStatus, persisted.Outcome);
        Assert.Equal(scenario.ExpectedOutcomeKey, persisted.OutcomeKey);
        Assert.Equal(scenario.ExpectedRuleId, persisted.MatchedRuleId);
    }

    // --- A processed message records a metric row carrying the decision status ---

    [Fact]
    public async Task Pipeline_RecordsMetric_WithDecisionStatusPopulated()
    {
        // A scenario with a known, non-Scrutiny decision so we can assert the exact status.
        var scenario = new OutcomeScenario(
            "Metric-Inclusion-AutoApproved", "Include", "KS4June",
            Answers: [],
            DecisionStatus.AutoApproved, "INC-ACC",
            PupilPincl: 402);
        var reference = scenario.ReferenceNumber;

        await using (var seedContext = _fixture.CreateContext())
        {
            if (!await seedContext.CheckingWindows.AnyAsync(w => w.Id == WindowId))
            {
                seedContext.CheckingWindows.Add(NewCheckingWindow());
                await seedContext.SaveChangesAsync();
            }

            seedContext.ChangeRequests.RemoveRange(
                seedContext.ChangeRequests.Where(r => r.ReferenceNumber == reference));
            await seedContext.QueueMetricEvents
                .Where(e => e.ReferenceNumber == reference)
                .ExecuteDeleteAsync();
            await seedContext.SaveChangesAsync();
            seedContext.ChangeRequests.Add(NewChangeRequest(reference));
            await seedContext.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var queueService = new PostgresQueueService(context);
            var rulesProvider = Substitute.For<IRulesProvider>();
            rulesProvider.Current.Returns(Snapshot);
            var sink = new DbMetricsSink(context);

            var sut = new RulesConsumer(
                queueService,
                rulesProvider,
                new RulesEngineImpl(),
                new RuleContextMapper(),
                context,
                sink);

            await sut.ProcessMessageBodyAsync(scenario.MessageJson, CancellationToken.None);

            // The hosting loop records the metric after a successful ack, mirrored here.
            var message = new QueueMessage
            {
                Id = Guid.NewGuid(),
                QueueName = QueueOptions.RulesEngineQueue,
                Payload = scenario.MessageJson,
                Attempts = 1,
                EnqueuedAt = DateTime.UtcNow.AddSeconds(-2),
            };
            await sut.RecordMetricSafelyAsync(message, deadLettered: false, CancellationToken.None);
        }

        await using var verify = _fixture.CreateContext();
        var metric = await verify.QueueMetricEvents
            .AsNoTracking()
            .SingleAsync(e => e.ReferenceNumber == reference);

        Assert.Equal(MetricStages.RulesEvaluated, metric.Stage);
        Assert.Equal(DecisionStatus.AutoApproved.ToString(), metric.DecisionStatus);
    }

    // --- scenarios ---------------------------------------------------------

    private static IEnumerable<OutcomeScenario> BuildScenarios()
    {
        // RequestTypeCode carries the producer's contract string: the WhatToChange
        // enum name, plus " - {reason option value}" where the flow has a
        // useAsRequestType question. Question ids and answer values mirror the
        // authored flow configs (see AnswerFieldMap and Web/Data/QuestionFlows).
        yield return new("Inclusion-AutoApproved", "Include", "KS4June",
            Answers: [],
            DecisionStatus.AutoApproved, "INC-ACC",
            PupilPincl: 402); // inclusionFlag comes from the pupil record, not an answer

        yield return new("AdmittedFollowingPermanentExclusion-PreCutoff", "Remove - permanent-exclusion", "KS4June",
            Answers: [("date-pupil-excluded", "2022-01-01")],
            DecisionStatus.AutoRejected, "AFE-PRE2023");

        yield return new("AdmittedFromAbroadEal-FirstLanguageEnglish", "Remove - english-not-first-language", "KS4June",
            Answers: [("first-language", "english")],
            DecisionStatus.AutoRejected, "EAL-REJ-ENG");

        yield return new("MergePupils-Default", "Merge", "KS4June",
            Answers: [],
            DecisionStatus.Scrutiny, "MRG-DEF");

        // The journey collects one social-care reason radio, so the rules' "all three
        // flags false" branch can never fire from journey data; the sat-exams
        // disjunct is the reachable auto-reject path.
        yield return new("SocialCareInvolvement-Ks4SatExams", "Remove - social-care-involvement", "KS4June",
            Answers:
            [
                ("social-care-reason", "police-involvement"),
                ("sat-exams", "yes"),
            ],
            DecisionStatus.AutoRejected, "SCI-KS4-REJ");

        yield return new("TerminalCriticalIllness-TerminalIsScrutiny", "Remove - life-limiting-illness", "KS4June",
            Answers: [("life-limiting-illness-health-issue", "life-limiting")],
            DecisionStatus.Scrutiny, "TCI-TERM-SCR");

        yield return new("YearGroupChange-Lower", "Remove - year-group-change", "KS4June",
            Answers: [("higher-lower", "lower")],
            DecisionStatus.AutoApproved, "YGC-LOWER");

        yield return new("Deceased-AutoApproved", "Remove - pupil-died", "KS4June",
            Answers: [],
            DecisionStatus.AutoApproved, "DEC-1");

        yield return new("ElectiveHomeEducation-Ks4PostCutoff", "Remove - elective-home-education", "KS4June",
            Answers: [("date-removed-from-roll", "2025-02-01")],
            DecisionStatus.AutoRejected, "EHE-KS4");

        yield return new("MovedSchoolDualRegistration-Default", "Remove - dual-registered-moved", "KS4June",
            Answers: [],
            DecisionStatus.Scrutiny, "MSD-DEF");

        yield return new("NotOnRoll-NonPost16", "Remove - not-on-roll", "KS4June",
            Answers: [],
            DecisionStatus.AutoApproved, "NOR-NONPOST16");

        yield return new("PermanentlyExcludedFromCurrentSchool-Ks4PostCutoff", "Remove - permanently-excluded", "KS4June",
            Answers: [("date-permanently-excluded", "2025-02-01")],
            DecisionStatus.AutoRejected, "PEX-KS4");

        yield return new("PermanentlyLeftEngland-Ks4PostCutoff", "Remove - permanently-left-england", "KS4June",
            Answers: [("date-removed-from-roll", "2025-06-01")],
            DecisionStatus.AutoRejected, "PLE-KS4");

        // The authored flow asks why-removed + date, not the whereabouts questions
        // the PMIE-REJ rule reads, so journey submissions always land in Scrutiny.
        yield return new("PupilMissingInEducation-DefaultsToScrutiny", "Remove - child-missing-education", "KS4June",
            Answers: [("why-removed", "no-agreed-leave-or-reason")],
            DecisionStatus.Scrutiny, "PMIE-DEF");

        // Per the "always Scrutiny on doubt" policy: a reason the AnswerFieldMap
        // cannot route must surface to a human, not be dropped.
        yield return new("UnroutedWhatToChange-FallsToScrutiny", "Remove - some-future-reason", "KS4June",
            Answers: [],
            DecisionStatus.Scrutiny, "_unmatched_outcome");
    }

    // --- pending outcomes (no journey flow yet) -----------------------------

    /// <summary>
    /// Outcomes seeded in rules.json whose journey flows are not authored yet, so no
    /// producer contract string routes to them (see
    /// <c>SeedRulesValidationTests.PendingJourneyOutcomeKeys</c>). Their rule branches
    /// are still exercised against the seed by driving the engine directly; move a
    /// scenario up into <see cref="BuildScenarios"/> when its flow exists.
    /// </summary>
    public static TheoryData<string, string, DecisionStatus, string> PendingOutcomeScenarios() => new()
    {
        { "CompletedKs4Elsewhere",   "KS4June",    DecisionStatus.Scrutiny,     "CKS4-DEF" },
        { "AssessmentsDeferred",     "KS2",    DecisionStatus.AutoRejected, "ASD-REJ"  },
        { "PupilAddedAfterSummerTerm", "KS4June",  DecisionStatus.AutoRejected, "PAS-REJ"  },
        { "PupilNotOnJuneList",      "KS4June",    DecisionStatus.Scrutiny,     "PNJL-DEF" },
        { "NotAtEndOf16To18Study",   "Post16", DecisionStatus.AutoApproved, "N16-LT18" },
        { "Other",                   "KS4June",    DecisionStatus.Scrutiny,     "OTH-DEF"  },
    };

    [Theory]
    [MemberData(nameof(PendingOutcomeScenarios))]
    public void Engine_RoutesPendingOutcome_ToExpectedDecision(
        string outcomeKey, string checkingWindowType, DecisionStatus expectedStatus, string expectedRuleId)
    {
        var ctx = new RuleContext(outcomeKey, checkingWindowType, new Dictionary<string, FieldValue>
        {
            ["checkingWindowType"]    = new FieldValue.Str(checkingWindowType),
            ["isContinuingKS2Studies"] = new FieldValue.Bool(false),
            ["dateAddedToRoll"]       = new FieldValue.Date(new DateOnly(2024, 1, 1)),
            ["pupilAge"]              = new FieldValue.Num(16),
        });

        var decision = new RulesEngineImpl().Evaluate(Snapshot.Rules, ctx, Snapshot.Lookups);

        Assert.Equal(expectedStatus, decision.Status);
        Assert.Equal(expectedRuleId, decision.MatchedRuleId);
    }

    // --- wiring ------------------------------------------------------------

    private static readonly Guid WindowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static CheckingWindow NewCheckingWindow() => new()
    {
        Id = WindowId,
        StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        Title = "Test Window",
    };

    private static ChangeRequest NewChangeRequest(string reference) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = WindowId,
        OrganisationUrn = 123456,
        PupilUpn = "UPN1",
        PupilFirstname = "Bob",
        PupilSurname = "Smith",
        Submitted = new DateTime(2026, 5, 14, 10, 0, 0, DateTimeKind.Unspecified),
        SubmittedById = Guid.NewGuid(),
        SubmittedByName = "Alice",
        Status = RequestStatus.SubmittedUnCommitted,
        ReferenceNumber = reference,
        RequestType = RequestType.Amendment, 
        RequestTypeDescription = "Amendment"
    };

    private static RulesSnapshot LoadSeedSnapshot()
    {
        var seedDir = LocateSeedDirectory();

        var rulesJson = File.ReadAllText(Path.Combine(seedDir, "rules.json"));
        var rules = JsonSerializer.Deserialize<RuleSet>(rulesJson, RulesJson.Options)
            ?? throw new InvalidOperationException("Seed rules.json did not deserialise.");
        var validation = new RuleSetValidator().Validate(rules);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Seed rules.json failed validation:\n  " + string.Join("\n  ", validation.Errors));
        }

        var lookupsJson = File.ReadAllText(Path.Combine(seedDir, "country-languages.json"));
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lookupsJson, RulesJson.Options)
            ?? throw new InvalidOperationException("Seed country-languages.json did not deserialise.");
        var countryLanguages = raw
            .Where(kvp => !kvp.Key.StartsWith('_') && kvp.Value.ValueKind == JsonValueKind.Array)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value.EnumerateArray()
                    .Select(e => e.GetString()!)
                    .ToArray());

        return new RulesSnapshot(
            validation.ResolvedRules!,
            new Lookups(countryLanguages),
            rules.Version,
            DateTimeOffset.UtcNow,
            RulesHealth.Healthy);
    }

    private static string LocateSeedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DfE.CheckPerformanceData.RulesEngineWorker", "seed");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/DfE.CheckPerformanceData.RulesEngineWorker/seed/ from " + AppContext.BaseDirectory);
    }

    public sealed record OutcomeScenario(
        string Name,
        string RequestTypeCode,
        string CheckingWindowType,
        (string QuestionId, string Value)[] Answers,
        DecisionStatus ExpectedStatus,
        string ExpectedRuleId,
        int PupilAge = 12,
        int PupilPincl = 0)
    {
        // The ReferenceNumber column is capped at 50 chars, so key off a stable
        // hash of the scenario name rather than the (sometimes long) name itself.
        public string ReferenceNumber => "REF-" + StableHash(Name).ToString("X8");

        private static uint StableHash(string value)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var c in value)
            {
                hash = (hash ^ c) * prime;
            }
            return hash;
        }

        public string ExpectedOutcomeKey =>
            AnswerFieldMap.WhatToChangeToOutcomeKey.TryGetValue(RequestTypeCode, out var key)
                ? key
                : AnswerFieldMap.UnknownOutcomeKey;

        public string MessageJson
        {
            get
            {
                // The producer writes the engine-facing value to RawValue and the
                // display label to Value; the mapper must prefer RawValue, so the
                // scenarios put a deliberately useless label in Value.
                var answersJson = string.Join(",\n      ", Answers.Select(a =>
                    $"{{ \"QuestionId\": \"{a.QuestionId}\", \"QuestionTitle\": \"{a.QuestionId}\", \"Type\": \"text\", \"Value\": \"display label\", \"RawValue\": \"{a.Value}\" }}"));

                return $$"""
                    {
                      "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
                      "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                      "CheckingWindowType": "{{CheckingWindowType}}",
                      "RequestTypeCode": "{{RequestTypeCode}}",
                      "School": { "Urn": "123456", "Name": "Test School" },
                      "SubmittedBy": { "UserId": "u1", "DisplayName": "Alice" },
                      "Pupil": { "Id": "p1", "CypmdId": "c1", "Firstname": "Bob", "Surname": "Smith", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": {{PupilAge}}, "Upn": "UPN1", "Pincl": {{PupilPincl}} },
                      "Answers": [
                          {{answersJson}}
                      ],
                      "ReferenceNumber": "{{ReferenceNumber}}",
                      "SubmittedAt": "2026-05-14T10:00:00Z"
                    }
                    """;
            }
        }

        public override string ToString() => Name;
    }
}
