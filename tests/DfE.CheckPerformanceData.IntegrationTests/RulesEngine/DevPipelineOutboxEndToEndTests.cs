using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.RulesEngineWorker.Zendesk;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;
using RulesConsumer = DfE.CheckPerformanceData.RulesEngineWorker.Consumers.RulesConsumer;
using ZendeskConsumer = DfE.CheckPerformanceData.RulesEngineWorker.Consumers.ZendeskConsumer;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesEngine;

/// <summary>
/// End-to-end coverage of the development harness sink: a request shaped exactly as the dev
/// submit endpoint builds it flows through the real <see cref="RulesConsumer"/> (decision
/// persisted, ticket message enqueued) and then the real <see cref="ZendeskConsumer"/> wired
/// to the development <see cref="DevOutboxZendeskService"/>. The captured ticket must land in
/// the shared <c>dev_zendesk_outbox</c> table so the web viewer can observe it cross-process.
/// </summary>
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class DevPipelineOutboxEndToEndTests
{
    private static readonly RulesSnapshot Snapshot = LoadSeedSnapshot();
    private static readonly Guid WindowId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");

    private readonly PostgresFixture _fixture;

    public DevPipelineOutboxEndToEndTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SubmittedRequest_FlowsThroughPipeline_AndCapturesOutboxTicket()
    {
        var reference = "DEV-" + Guid.NewGuid().ToString("N")[..8];

        await using (var seed = _fixture.CreateContext())
        {
            if (!await seed.CheckingWindows.AnyAsync(w => w.Id == WindowId))
            {
                seed.CheckingWindows.Add(NewCheckingWindow());
                await seed.SaveChangesAsync();
            }

            seed.DevZendeskTickets.RemoveRange(seed.DevZendeskTickets.Where(t => t.ReferenceNumber == reference));
            seed.ChangeRequests.RemoveRange(seed.ChangeRequests.Where(r => r.ReferenceNumber == reference));
            await seed.SaveChangesAsync();

            seed.ChangeRequests.Add(NewChangeRequest(reference));
            await seed.SaveChangesAsync();
        }

        var messageJson = ApprovedRequestJson(reference);

        // Stage 1: rules consumer evaluates, persists the decision and enqueues a ticket message.
        await using (var context = _fixture.CreateContext())
        {
            var queueService = new PostgresQueueService(context);
            var rulesProvider = Substitute.For<IRulesProvider>();
            rulesProvider.Current.Returns(Snapshot);

            var rulesConsumer = new RulesConsumer(
                queueService,
                rulesProvider,
                new RulesEngineImpl(),
                new RuleContextMapper(),
                context);

            await rulesConsumer.ProcessMessageBodyAsync(messageJson, CancellationToken.None);
        }

        // Stage 2: zendesk consumer builds the ticket and the dev fake captures it to the outbox.
        await using (var context = _fixture.CreateContext())
        {
            var queueService = new PostgresQueueService(context);
            var devZendesk = new DevOutboxZendeskService(context);
            var zendeskConsumer = new ZendeskConsumer(queueService, devZendesk, context);

            await zendeskConsumer.ProcessMessageBodyAsync(messageJson, CancellationToken.None);
        }

        await using var verify = _fixture.CreateContext();

        var decision = await verify.ChangeRequests.AsNoTracking()
            .SingleAsync(r => r.ReferenceNumber == reference);
        Assert.Equal(DecisionStatus.AutoApproved, decision.DecisionStatus);

        var captured = await verify.DevZendeskTickets.AsNoTracking()
            .SingleAsync(t => t.ReferenceNumber == reference);
        Assert.Equal(reference, captured.ReferenceNumber);
        Assert.Contains("Auto-Approved", captured.Subject);
        Assert.True(captured.TicketId > 0);
        Assert.False(string.IsNullOrWhiteSpace(captured.RawJson));
    }

    private static CheckingWindow NewCheckingWindow() => new()
    {
        Id = WindowId,
        StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
        KeyStage = KeyStages.KS4,
        CheckingWindowType = CheckingWindowType.KS4June,
        Title = "Dev Harness Test Window",
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
        SubmittedByName = "Dev Harness",
        Status = RequestStatus.SubmittedUnCommitted,
        ReferenceNumber = reference,
        RequestType = "change",
    };

    // The approved preset the dev submit endpoint uses: Inclusion with an accepted status flag.
    private static string ApprovedRequestJson(string reference) => $$"""
        {
          "CheckingWindowId": "{{WindowId}}",
          "CheckingWindowType": "KS4",
          "WhatToChange": "Inclusion",
          "School": { "Urn": "123456", "Name": "Dev Harness School" },
          "SubmittedBy": { "UserId": "dev", "DisplayName": "Dev Harness" },
          "Pupil": { "Id": "p1", "CypmdId": "c1", "Firstname": "Bob", "Surname": "Smith", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": 12, "Upn": "UPN1" },
          "Answers": [
              { "QuestionId": "inclusion-status-flag", "QuestionTitle": "inclusion-status-flag", "Type": "text", "Value": "402" }
          ],
          "ReferenceNumber": "{{reference}}",
          "SubmittedAt": "2026-05-14T10:00:00Z"
        }
        """;

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
}
