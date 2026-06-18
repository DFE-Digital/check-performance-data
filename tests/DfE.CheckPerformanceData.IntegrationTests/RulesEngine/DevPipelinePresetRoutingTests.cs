using System.Text.Json;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RulesEngineImpl = DfE.CheckPerformanceData.Application.RulesEngine.RulesEngine;
using RulesConsumer = DfE.CheckPerformanceData.RulesEngineWorker.Consumers.RulesConsumer;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesEngine;

/// <summary>
/// Guards the dev-harness presets against vocabulary drift. The rules-engine rework renamed the
/// producer contract (e.g. "Include", "KS4June") and moved inclusion onto the pupil's Pincl, which
/// silently turned every preset into a Scrutiny fall-through. This drives the real
/// <see cref="DevPipelineRunner"/> — the same path behind the dev pipeline trigger and the UAT
/// drive-traffic buttons — through the real <see cref="RulesConsumer"/> and asserts each preset
/// still routes to the decision it advertises. If a preset's RequestTypeCode, checking-window type
/// or Pincl regresses, the matching decision no longer lands and this fails.
/// </summary>
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class DevPipelinePresetRoutingTests
{
    private static readonly RulesSnapshot Snapshot = LoadSeedSnapshot();

    private readonly PostgresFixture _fixture;

    public DevPipelinePresetRoutingTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("approved", DecisionStatus.AutoApproved)]
    [InlineData("rejected", DecisionStatus.AutoRejected)]
    [InlineData("scrutiny", DecisionStatus.Scrutiny)]
    public async Task DevPreset_DrivenThroughRealRunner_RoutesToAdvertisedDecision(
        string outcome, DecisionStatus expected)
    {
        string reference;

        // Drive the real dev harness: seeds a ChangeRequest + dev window and enqueues the document.
        await using (var context = _fixture.CreateContext())
        {
            var queueService = new PostgresQueueService(context);
            var runner = new DevPipelineRunner(context, queueService);
            var result = await runner.SubmitAsync(outcome, CancellationToken.None);
            reference = result.Reference;

            // The preset must advertise the decision we expect, or the demo lies to the operator.
            Assert.Equal(expected.ToString(), result.ExpectedDecision);
        }

        // Drain the queue through the real consumer until our reference is decided. Processing any
        // unrelated leftover message is harmless: the write-back updates by reference, so a row that
        // is not ours simply matches nothing.
        await using (var context = _fixture.CreateContext())
        {
            var queueService = new PostgresQueueService(context);
            var rulesProvider = Substitute.For<IRulesProvider>();
            rulesProvider.Current.Returns(Snapshot);
            var sut = new RulesConsumer(
                queueService, rulesProvider, new RulesEngineImpl(), new RuleContextMapper(), context);

            for (var i = 0; i < 25; i++)
            {
                var message = await queueService.DequeueAsync(QueueOptions.RulesEngineQueue, CancellationToken.None);
                if (message is null) break;
                await sut.ProcessMessageBodyAsync(message.Payload, CancellationToken.None);

                var decided = await context.ChangeRequests.AsNoTracking()
                    .AnyAsync(r => r.ReferenceNumber == reference && r.Outcome != null);
                if (decided) break;
            }
        }

        await using var verify = _fixture.CreateContext();
        var persisted = await verify.ChangeRequests.AsNoTracking()
            .SingleAsync(r => r.ReferenceNumber == reference);
        Assert.Equal(expected, persisted.Outcome);
    }

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
