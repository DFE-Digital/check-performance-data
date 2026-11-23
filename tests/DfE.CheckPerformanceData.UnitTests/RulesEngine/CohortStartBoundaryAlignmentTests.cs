using System.Text.Json;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// The journey and the rules engine apply the same boundary to the same field: the journey
/// validates <c>date-pupil-started</c> against the <c>cohort.start</c> fact (1 September four
/// years back from the clock), and the shipped seed's EAL new-admission branches compare
/// <c>schoolAdmissionDate</c> against a window-relative 1 September. A request the journey
/// accepts must also be acceptable to the rules engine, so for any one window year the fact
/// (answered in that year) and the seed boundary (resolved against a window anchored in that
/// year) must be the same date. Each side is pinned on its own (CohortStartDateFactTests,
/// Ks4RemovalRulesTests); this test pins them against each other, so a change to either side
/// fails here unless the other is changed in the same pull request.
/// </summary>
public sealed class CohortStartBoundaryAlignmentTests
{
    private static readonly JourneyConditionContext Context = new()
    {
        Journey = new RequestState(),
        User = new JourneyUserContext()
    };

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    public void JourneyCohortStart_EqualsSeededEalNewAdmissionBoundary_ForOneWindow(int windowYear)
    {
        // Journey side: the fact a school answering during that year is validated against.
        var fact = new CohortStartDateFact().Value(Context, new DateOnly(windowYear, 6, 15));

        // Rules side: the shipped seed's EAL new-admission cutoff, resolved against a window
        // whose PupilData exercise starts in the same year.
        var spec = FindFirstSeptemberSpec(
            LoadSeedRules().Outcomes.SelectMany(o => o.Rules)
                .Single(b => b.Id == "EAL-ACC-NEWADM-2022-ALL").When);

        Assert.True(
            RelativeDateResolver.TryResolve(spec, new DateOnly(windowYear, 1, 1), out var boundary),
            "The seed's EAL new-admission boundary did not resolve for the window year.");

        Assert.Equal(fact, boundary);
    }

    // --- helpers ---

    private static RelativeDateSpec? FindFirstSeptemberSpec(Predicate predicate)
    {
        switch (predicate)
        {
            case Predicate.AllOf all:
            {
                foreach (var item in all.Items)
                {
                    var found = FindFirstSeptemberSpec(item);
                    if (found is not null) return found;
                }
                return null;
            }
            case Predicate.AnyOf any:
            {
                foreach (var item in any.Items)
                {
                    var found = FindFirstSeptemberSpec(item);
                    if (found is not null) return found;
                }
                return null;
            }
            case Predicate.Not not:
                return FindFirstSeptemberSpec(not.Inner);
            case Predicate.FieldCompare cmp when cmp.Value is FieldValue.RelativeDate rd:
                return rd.Spec;
            default:
                return null;
        }
    }

    private static RuleSet LoadSeedRules()
    {
        var json = File.ReadAllText(Path.Combine(LocateSeedDirectory(), "rules.json"));
        var parsed = JsonSerializer.Deserialize<RuleSet>(json, RulesJson.Options)!;
        var validation = new RuleSetValidator().Validate(parsed);
        Assert.True(validation.IsValid, string.Join("\n  ", validation.Errors));
        return validation.ResolvedRules!;
    }

    private static string LocateSeedDirectory()
    {
        var relative = Path.Combine("src", "DfE.CheckPerformanceData.RulesEngineWorker", "seed");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate {relative} from {AppContext.BaseDirectory}.");
    }
}
