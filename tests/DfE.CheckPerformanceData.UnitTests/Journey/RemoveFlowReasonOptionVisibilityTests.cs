using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Issue 508 — the KS4 June Remove reason "Admitted following permanent exclusion (not registered
// independent schools)" is offered to every school, including independent schools, which cannot
// action it. This exercises the REAL flow JSON against the REAL condition set through the REAL
// OptionVisibilityService, so it covers the composition the option-gating mechanism actually
// depends on — not a substituted service.
//
// The fail-closed hazard this file exists to catch: OptionVisibilityService hides any option whose
// condition name is not registered. So if `SchoolIsNotIndependent` reaches the JSON without reaching
// DependencyManager, the reason disappears for EVERY school — the opposite of the fix, and silent.
// The type-1/type-10/null assertions below are the ones that fail in that state.
public class RemoveFlowReasonOptionVisibilityTests
{
    private const string PermanentExclusion = "permanent-exclusion";
    private const string NotOnRoll = "not-on-roll";
    private const string ExpectedPermanentExclusionLabel =
        "Admitted following permanent exclusion";

    private const int AddBackPincl = 403;
    private const int IncludedPincl = 401;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Every <see cref="IJourneyCondition"/> in the Application assembly, instantiated the same way
    /// QuestionFlowValidatorAlignmentTests does. The running app supplies conditions through DI,
    /// so this set alone does not prove registration — EveryImplementedCondition_IsRegisteredInTheContainer
    /// closes that gap.
    /// </summary>
    private static readonly IReadOnlyList<IJourneyCondition> AllConditions =
        typeof(IJourneyCondition).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IJourneyCondition).IsAssignableFrom(t))
            .Select(t => (IJourneyCondition)CreateWithStubbedServices(t))
            .ToList();

    /// <summary>
    /// Builds a condition the way the composition test needs to call it - with Evaluate usable -
    /// even when its constructor takes services. Activator.CreateInstance is not enough: a
    /// condition with a dependency has no parameterless constructor, and a substituted collection
    /// property returns null, which the condition then enumerates and throws on.
    ///
    /// Only the college list needs a real value. Its own behaviour is covered by
    /// SchoolCanRecordNotOnRollConditionTests; here an empty list is the right stub, because this
    /// test is about which options the school type gate hides, not about which colleges are listed.
    /// </summary>
    private static object CreateWithStubbedServices(Type type)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

        if (constructor.GetParameters().Length == 0)
            return Activator.CreateInstance(type)!;

        var arguments = constructor.GetParameters().Select(p => p.ParameterType switch
        {
            var t when t == typeof(INotOnRollCollegeListProvider) => new NoColleges(),
            _ => Substitute.For([p.ParameterType], []),
        }).ToArray();

        return constructor.Invoke(arguments);
    }

    private sealed class NoColleges : INotOnRollCollegeListProvider
    {
        public NotOnRollCollegeList Current => NotOnRollCollegeList.Empty;
    }

    private static Question TheReasonQuestion()
    {
        var config = JsonSerializer.Deserialize<QuestionFlowConfig>(
            File.ReadAllText(RemoveKs4JunePath()), JsonOptions)!;

        var page = config.Pages.Single(p => p.Id == "reason");
        return page.Questions.Single(q => q.Id == "reason");
    }

    private static string RemoveKs4JunePath() =>
        Path.Combine(LocateFlowsDirectory(), "Remove_KS4June.json");

    private static string LocateFlowsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/DfE.CheckPerformanceData.Web/Data/QuestionFlows from " +
            AppContext.BaseDirectory);
    }

    private static JourneyConditionContext Context(string? typeId, int pincl = IncludedPincl) => new()
    {
        Journey = new RequestState
        {
            SelectedPupil = new PupilDto
            {
                Id = Guid.NewGuid(), Firstname = "Alice", Surname = "Smith",
                Sex = "F", DateOfBirth = "01/09/2010", Age = 15,
                Cypmd_Id = "C1", Identifier = "U1", Pincl = pincl
            }
        },
        User = new JourneyUserContext { OrganisationTypeId = typeId }
    };

    private static IReadOnlyList<QuestionOption> Visible(string? typeId, int pincl = IncludedPincl) =>
        new OptionVisibilityService(AllConditions).GetVisibleOptions(TheReasonQuestion(), Context(typeId, pincl));

    private static IReadOnlyList<string> VisibleValues(string? typeId, int pincl = IncludedPincl) =>
        Visible(typeId, pincl).Select(o => o.Value).ToList();

    // ── FR-001: an independent school is not offered the reason ────────────────

    [Fact]
    public void PermanentExclusion_IsHidden_ForIndependentSchoolType11()
    {
        Assert.DoesNotContain(PermanentExclusion, VisibleValues("11"));
    }

    // ── FR-002 / FR-009: schools that are not independent schools are unaffected ──

    [Fact]
    public void PermanentExclusion_IsShown_ForMaintainedSchoolType1()
    {
        Assert.Contains(PermanentExclusion, VisibleValues("1"));
    }

    [Fact]
    public void PermanentExclusion_IsShown_ForOtherIndependentSpecialSchoolType10()
    {
        // Type 10 is deliberately not independent (ticket 281165), so it keeps the reason.
        Assert.Contains(PermanentExclusion, VisibleValues("10"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PermanentExclusion_IsShown_WhenOrganisationTypeIsAbsent(string? typeId)
    {
        Assert.Contains(PermanentExclusion, VisibleValues(typeId));
    }

    // ── FR-004 / SC-003: the gate removes this option and nothing else ────────

    [Fact]
    public void OnlyPermanentExclusionAndNotOnRoll_DifferBetweenSchoolTypes()
    {
        var maintained = VisibleValues("1").ToHashSet(StringComparer.Ordinal);
        var independent = VisibleValues("11").ToHashSet(StringComparer.Ordinal);

        // permanent-exclusion moves one way, not-on-roll the other. Anything else that differs
        // means the new gate is wider than intended.
        Assert.Equal([PermanentExclusion], maintained.Except(independent).OrderBy(v => v));
        Assert.Equal([NotOnRoll], independent.Except(maintained).OrderBy(v => v));
    }

    [Fact]
    public void NotOnRoll_RemainsOfferedToIndependentSchoolsOnly()
    {
        // The pre-existing independent-only reason must keep working — this change adds a second
        // independent-related reason in the opposite polarity, and the two must not collide.
        Assert.Contains(NotOnRoll, VisibleValues("11"));
        Assert.DoesNotContain(NotOnRoll, VisibleValues("1"));
    }

    // ── The add-back half of the AND still bites (FR-004) ─────────────────────

    [Theory]
    [InlineData("1")]
    [InlineData("11")]
    public void PermanentExclusion_IsHidden_ForAddBackPupil(string typeId)
    {
        // Pincl 403 (added back into the school's results) fails PupilIsNotAddBack, so the reason
        // stays hidden regardless of school type. Guards against the new array dropping its
        // first element.
        Assert.DoesNotContain(PermanentExclusion, VisibleValues(typeId, AddBackPincl));
    }

    [Fact]
    public void AddBackPupil_StillSeesTheAddBackReasons()
    {
        var visible = VisibleValues("1", AddBackPincl);
        Assert.Contains("completed-ks4-elsewhere", visible);
        Assert.Contains("other", visible);
        Assert.DoesNotContain(PermanentExclusion, visible);
    }

    // ── FR-010: the pinned strings are untouched by the gate ──────────────────

    [Fact]
    public void PermanentExclusionOption_PinsLabelValueAndNextPage()
    {
        var option = TheReasonQuestion().Options!.Single(o => o.Value == PermanentExclusion);

        Assert.Equal(ExpectedPermanentExclusionLabel, option.Label);
        Assert.Equal("permanent-exclusion", option.NextPageId);
    }

    [Fact]
    public void PermanentExclusionOption_GatedOnNotAddBackAndNotIndependent()
    {
        var option = TheReasonQuestion().Options!.Single(o => o.Value == PermanentExclusion);

        Assert.Equal(["PupilIsNotAddBack", "SchoolIsNotIndependent"], option.VisibleWhen);
    }
}
