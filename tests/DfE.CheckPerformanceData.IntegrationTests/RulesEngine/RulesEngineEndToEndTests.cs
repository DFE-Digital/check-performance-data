using System.Text.Json;
using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;
using WorkerHost = DfE.CheckPerformanceData.RulesEngineWorker;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesEngine;

/// <summary>
/// End-to-end coverage of the worker pipeline: queue-shaped JSON →
/// <see cref="WorkerHost.RulesEngineWorker.ProcessMessageBodyAsync"/> →
/// real <see cref="RuleContextMapper"/> → real <see cref="RulesEngineImpl"/> →
/// captured <see cref="Decision"/> handed to a stubbed
/// <see cref="IRequestDecisionHandler"/>.
///
/// Rules and lookups come from the seed JSON shipped at
/// <c>src/DfE.CheckPerformanceData.RulesEngineWorker/seed/</c> — the same files
/// <c>BlobRulesProvider</c> loads in production. One happy-path scenario per
/// Stage 1 outcome guards against drift in routing or per-table evaluation.
///
/// No Postgres fixture: the worker's pipeline has no DB writes (the handler
/// produces Zendesk tickets), so spinning up a container would prove nothing.
/// </summary>
public sealed class RulesEngineEndToEndTests
{
    private static readonly RulesSnapshot Snapshot = LoadSeedSnapshot();

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
        Decision? captured = null;
        var handler = Substitute.For<IRequestDecisionHandler>();
        handler
            .When(h => h.HandleAsync(Arg.Any<RequestDocument>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>()))
            .Do(call => captured = call.ArgAt<Decision>(1));

        var sut = NewWorker(handler);

        await sut.ProcessMessageBodyAsync(scenario.MessageJson, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(scenario.ExpectedStatus, captured!.Status);
        Assert.Equal(scenario.ExpectedRuleId, captured.MatchedRuleId);
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

    private static WorkerHost.RulesEngineWorker NewWorker(IRequestDecisionHandler handler)
    {
        var queueServiceClient = Substitute.For<QueueServiceClient>();
        queueServiceClient.GetQueueClient("test-queue").Returns(Substitute.For<QueueClient>());

        var options = Options.Create(new WorkerHost.RulesEngineOptions
        {
            QueueName = "test-queue",
            MaxMessagesPerPoll = 1,
            EmptyQueueDelayMs = 0,
            RetryDelayMs = 0,
            MaxDequeueCount = 5,
        });

        var rulesProvider = Substitute.For<IRulesProvider>();
        rulesProvider.Current.Returns(Snapshot);

        // The worker resolves the (scoped) handler and outcome repository per
        // message via the scope factory. The outcome write is covered by
        // DecisionOutcomeRepositoryTests; here a stub keeps the pipeline DB-free.
        var outcomes = Substitute.For<IDecisionOutcomeRepository>();
        outcomes.RecordOutcomeAsync(Arg.Any<Guid>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        services.AddScoped(_ => outcomes);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new WorkerHost.RulesEngineWorker(
            NullLogger<WorkerHost.RulesEngineWorker>.Instance,
            queueServiceClient,
            options,
            scopeFactory,
            rulesProvider,
            new RulesEngineImpl(),
            new RuleContextMapper());
    }

    private static RulesSnapshot LoadSeedSnapshot()
    {
        var seedDir = LocateSeedDirectory();

        var rulesJson = System.IO.File.ReadAllText(Path.Combine(seedDir, "rules.json"));
        var rules = JsonSerializer.Deserialize<RuleSet>(rulesJson, RulesJson.Options)
            ?? throw new InvalidOperationException("Seed rules.json did not deserialise.");
        var validation = new RuleSetValidator().Validate(rules);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Seed rules.json failed validation:\n  " + string.Join("\n  ", validation.Errors));
        }

        var lookupsJson = System.IO.File.ReadAllText(Path.Combine(seedDir, "country-languages.json"));
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
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DfE.CheckPerformanceData.RulesEngineWorker", "seed");
            if (System.IO.Directory.Exists(candidate)) return candidate;
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
                      "ReferenceNumber": "REF-001",
                      "SubmittedAt": "2026-05-14T10:00:00Z"
                    }
                    """;
            }
        }

        public override string ToString() => Name;
    }
}
