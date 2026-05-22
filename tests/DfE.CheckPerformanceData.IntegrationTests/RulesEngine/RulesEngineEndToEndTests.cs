using System.Text.Json;
using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using DfE.CheckPerformanceData.Domain.QueueMessages;
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
            .When(h => h.HandleAsync(Arg.Any<RequestMessage>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>()))
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
        yield return new("Inclusion-AutoApproved", "Inclusion", "KS4",
            Answers: [("inclusion-status-flag", "402")],
            DecisionStatus.AutoApproved, "INC-ACC");

        yield return new("AdmittedFollowingPermanentExclusion-PreCutoff", "Admitted following permanent exclusion", "KS4",
            Answers: [("date-of-permanent-exclusion", "2022-01-01")],
            DecisionStatus.AutoRejected, "AFE-PRE2023");

        yield return new("AdmittedFromAbroadEal-FirstLanguageEnglish", "Admitted from abroad with English not first language", "KS4",
            Answers: [("first-language", "ENG")],
            DecisionStatus.AutoRejected, "EAL-REJ-ENG");

        yield return new("CompletedKs4Elsewhere-Default", "Completed KS4 studies this academic year in year 11 at another school or college", "KS4",
            Answers: [],
            DecisionStatus.Scrutiny, "CKS4-DEF");

        yield return new("MergePupils-Default", "Merge pupils", "KS4",
            Answers: [],
            DecisionStatus.Scrutiny, "MRG-DEF");

        yield return new("SocialCareInvolvement-Ks4AllFlagsFalse", "Social care involvement - including police/prison", "KS4",
            Answers:
            [
                ("social-care-involvement", "no"),
                ("recent-police-involvement", "no"),
                ("detained-in-prison", "no"),
            ],
            DecisionStatus.AutoRejected, "SCI-KS4-REJ");

        yield return new("TerminalCriticalIllness-TerminalIsScrutiny", "Terminal/Critical illness", "KS4",
            Answers: [("terminal-illness", "yes")],
            DecisionStatus.Scrutiny, "TCI-TERM-SCR");

        yield return new("YearGroupChange-Lower", "Year group change", "KS4",
            Answers: [("year-group-change", "Lower")],
            DecisionStatus.AutoApproved, "YGC-LOWER");

        yield return new("Deceased-AutoApproved", "Deceased", "KS4",
            Answers: [],
            DecisionStatus.AutoApproved, "DEC-1");

        yield return new("ElectiveHomeEducation-Ks4PostCutoff", "Elective home education", "KS4",
            Answers: [("date-of-removal-from-roll", "2025-02-01")],
            DecisionStatus.AutoRejected, "EHE-KS4");

        yield return new("MovedSchoolDualRegistration-Default", "Moved school/Dual registration", "KS4",
            Answers: [],
            DecisionStatus.Scrutiny, "MSD-DEF");

        yield return new("NotOnRoll-NonPost16", "Not on roll", "KS4",
            Answers: [],
            DecisionStatus.AutoApproved, "NOR-NONPOST16");

        yield return new("PermanentlyExcludedFromCurrentSchool-Ks4PostCutoff", "Permanently excluded from current school", "KS4",
            Answers: [("date-of-permanent-exclusion", "2025-02-01")],
            DecisionStatus.AutoRejected, "PEX-KS4");

        yield return new("PermanentlyLeftEngland-Ks4PostCutoff", "Permanently left England", "KS4",
            Answers: [("date-of-removal-from-roll", "2025-06-01")],
            DecisionStatus.AutoRejected, "PLE-KS4");

        yield return new("PupilMissingInEducation-WhereaboutsUnknown", "Pupil missing in Education", "KS4",
            Answers:
            [
                ("whereabouts-known", "no"),
                ("located-reasonable-efforts", "no"),
            ],
            DecisionStatus.AutoRejected, "PMIE-REJ");

        yield return new("AssessmentsDeferred-NotContinuing", "One or more end-of-key stage assessments deferred by a year", "KS2",
            Answers: [("continuing-ks2-studies", "no")],
            DecisionStatus.AutoRejected, "ASD-REJ");

        yield return new("PupilAddedAfterSummerTerm-Before", "Pupil added to school roll after start of summer term", "KS4",
            Answers: [("date-added-to-roll", "2024-01-01")],
            DecisionStatus.AutoRejected, "PAS-REJ");

        yield return new("PupilNotOnJuneList-Default", "Pupil not on June list", "KS4",
            Answers: [],
            DecisionStatus.Scrutiny, "PNJL-DEF");

        yield return new OutcomeScenario(
            Name: "NotAtEndOf16To18Study-Under18",
            WhatToChange: "Not at end of 16 to 18 study",
            CheckingWindowType: "Post16",
            Answers: [],
            ExpectedStatus: DecisionStatus.AutoApproved,
            ExpectedRuleId: "N16-LT18",
            PupilAge: 16);

        yield return new("Other-Default", "Other", "KS4",
            Answers: [],
            DecisionStatus.Scrutiny, "OTH-DEF");
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

        return new WorkerHost.RulesEngineWorker(
            NullLogger<WorkerHost.RulesEngineWorker>.Instance,
            queueServiceClient,
            options,
            handler,
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
        string WhatToChange,
        string CheckingWindowType,
        (string QuestionId, string Value)[] Answers,
        DecisionStatus ExpectedStatus,
        string ExpectedRuleId,
        int PupilAge = 12)
    {
        public string MessageJson
        {
            get
            {
                var answersJson = string.Join(",\n      ", Answers.Select(a =>
                    $"{{ \"QuestionId\": \"{a.QuestionId}\", \"QuestionTitle\": \"{a.QuestionId}\", \"Type\": \"text\", \"Value\": \"{a.Value}\" }}"));

                return $$"""
                    {
                      "RequestId": "11111111-2222-3333-4444-555555555555",
                      "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                      "CheckingWindowType": "{{CheckingWindowType}}",
                      "WhatToChange": "{{WhatToChange}}",
                      "Reason": "scenario {{Name}}",
                      "School": { "Urn": "123456", "Name": "Test School" },
                      "SubmittedBy": { "UserId": "u1", "DisplayName": "Alice" },
                      "Pupil": { "Id": "p1", "Firstname": "Bob", "Surname": "Smith", "Sex": "M", "Age": {{PupilAge}} },
                      "Answers": [
                          {{answersJson}}
                      ],
                      "Uploads": [],
                      "ReferenceNumber": "REF-001",
                      "Status": "submitted",
                      "SubmittedAt": "2026-05-14T10:00:00Z"
                    }
                    """;
            }
        }

        public override string ToString() => Name;
    }
}
