using System.Text.Json;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
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

        Assert.Equal(scenario.ExpectedStatus, persisted.DecisionStatus);
        Assert.Equal(scenario.ExpectedOutcomeKey, persisted.DecisionOutcomeKey);
        Assert.Equal(scenario.ExpectedRuleId, persisted.MatchedRuleId);
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
        RequestType = "change",
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
        string WhatToChange,
        string CheckingWindowType,
        (string QuestionId, string Value)[] Answers,
        DecisionStatus ExpectedStatus,
        string ExpectedRuleId,
        int PupilAge = 12)
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
            AnswerFieldMap.WhatToChangeToOutcomeKey.TryGetValue(WhatToChange, out var key)
                ? key
                : AnswerFieldMap.UnknownOutcomeKey;

        public string MessageJson
        {
            get
            {
                var answersJson = string.Join(",\n      ", Answers.Select(a =>
                    $"{{ \"QuestionId\": \"{a.QuestionId}\", \"QuestionTitle\": \"{a.QuestionId}\", \"Type\": \"text\", \"Value\": \"{a.Value}\" }}"));

                return $$"""
                    {
                      "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                      "CheckingWindowType": "{{CheckingWindowType}}",
                      "WhatToChange": "{{WhatToChange}}",
                      "School": { "Urn": "123456", "Name": "Test School" },
                      "SubmittedBy": { "UserId": "u1", "DisplayName": "Alice" },
                      "Pupil": { "Id": "p1", "CypmdId": "c1", "Firstname": "Bob", "Surname": "Smith", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": {{PupilAge}}, "Upn": "UPN1" },
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
