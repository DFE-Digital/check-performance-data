using System.Reflection;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// PrdCaseCoverageTests reflection-walks both search test assemblies and asserts every
// PRD §6 case letter A through P (excluding AC-P4, owned by Phase 1.10 SEARCH-X-01) has
// at least one method-level [Trait("prd-case", <letter>)] test. Companion Fact does the
// same for the seven PRD §9.1 [Trait("prd-filter", <slug>)] slugs. Any future test
// deletion, rename, or trait removal that would silently drop a PRD case from coverage
// fails CI naming the missing letter(s).
//
// PRD §6 case letter → intent map (as of 2026-07-22):
//   A single-word           SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   B multi-word OR         SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   C multi-word AND / OR   SearchTermNormalizerTests
//   D quoted phrase         SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   E negation              SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   F numbers / hyphens     PageNodeRepositorySearchTests + ContentBlockRepositorySearchTests
//   G very-short (<2)       SiteSearchServiceTests + ContentBlockSearchServiceTests
//   H long / 100-char cap   SearchControllerTests
//   I whitespace / empty    SearchTermNormalizerTests + SiteSearchMergedPagedTests + ContentBlockSearchServiceTests
//   J special / HTML        SearchTermNormalizerTests + SearchSnippetTests + ContentBlockRepositorySearchTests + ContentBlockSearchServiceTests
//   K duplicate blocks      ContentBlockRepositorySearchTests
//   L editor-suppressed     ContentBlockRepositorySearchTests + SiteSearchServiceFilterTests + ContentBlockSearchServiceTests
//   M keywords boost        PageNodeRepositorySearchTests + SiteSearchServiceRankingTests
//   N unpublished-target    PageNodeRepositorySearchTests + SiteSearchServiceFilterTests + ContentBlockSearchServiceTests
//   O scope filter          SiteSearchServiceTests
//   P degenerate / error    SiteSearchMergedPagedTests
//
// AC-P4 (DB unavailable → GDS 503) is deliberately NOT in ExpectedCases. Phase 1.10
// SEARCH-X-01 owns the integration coverage for that acceptance criterion.
//
// Landmine L-L: attributes are enumerated at the method level only via
// GetMethods(...).GetCustomAttributesData(). Class-level [Trait] attributes are invisible
// to this walk. Every relevant [Fact] / [Theory] must carry the trait on the method
// itself. Reading is via CustomAttributeData (not GetCustomAttributes<TraitAttribute>())
// because Xunit.TraitAttribute (2.9.3) does not expose Name/Value as public members —
// the ctor args are consumed only by the TraitDiscoverer at test-discovery time.
public sealed class PrdCaseCoverageTests
{
    private const string TraitAttributeFullName = "Xunit.TraitAttribute";

    // PRD §6.1 – §6.16 case letters (minus AC-P4 which Phase 1.10 SEARCH-X-01 owns).
    private static readonly HashSet<string> ExpectedCases =
        new(StringComparer.Ordinal)
        { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P" };

    // PRD §9.1 silent-filter slugs. Each must have ≥1 [Trait("prd-filter", <slug>)] test
    // proving the filter removes the target row(s) from search results.
    private static readonly HashSet<string> ExpectedFilters =
        new(StringComparer.Ordinal)
        {
            "admin-path",
            "e2e-key",
            "guidance-ks4-2026-nav-key",
            "contentblock-appearinsearch-false",
            "pagenode-appearinsearch-false",
            "draft-page",
            "unpublished-target",
        };

    [Fact]
    public void EveryPrdCase_A_Through_P_HasAtLeastOneTraitedTest_ExcludingAcP4()
    {
        var observed = SearchTestAssemblies()
            .SelectMany(asm => TraitPairs(asm))
            .Where(t => t.Name == "prd-case")
            .Select(t => t.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedCases.Except(observed).ToList();
        Assert.True(missing.Count == 0,
            $"PRD §6 cases without any [Trait(\"prd-case\", \"X\")] test: {string.Join(", ", missing)}. " +
            "AC-P4 (DB unavailable) is deliberately excluded — owned by Phase 1.10 SEARCH-X-01.");
    }

    [Fact]
    public void AllSevenPrdFilters_HaveAtLeastOneTraitedTest()
    {
        var observed = SearchTestAssemblies()
            .SelectMany(asm => TraitPairs(asm))
            .Where(t => t.Name == "prd-filter")
            .Select(t => t.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedFilters.Except(observed).ToList();
        Assert.True(missing.Count == 0,
            $"PRD §9.1 filters without any [Trait(\"prd-filter\", \"<slug>\")] test: {string.Join(", ", missing)}.");
    }

    private static IEnumerable<Assembly> SearchTestAssemblies() =>
    [
        typeof(SearchTermNormalizerTests).Assembly, // UnitTests (this project)
        LoadIntegrationTestsAssembly(),
    ];

    // Landmine L-C: the IntegrationTests DLL is not a project reference of UnitTests, so it
    // does not ship into the UnitTests test-host bin folder. Plain Assembly.Load(name) will
    // throw FileNotFoundException. Locate the sibling bin manually and load from disk.
    private static Assembly LoadIntegrationTestsAssembly()
    {
        try
        {
            return Assembly.Load("DfE.CheckPerformanceData.IntegrationTests");
        }
        catch (FileNotFoundException)
        {
            // Fall through — probe the sibling project's parallel bin/{Config}/{TFM} layout.
        }

        var unitBinDir = new FileInfo(typeof(PrdCaseCoverageTests).Assembly.Location).Directory
            ?? throw new XunitException(
                "PrdCaseCoverageTests: could not determine UnitTests bin directory from Assembly.Location.");

        // Expected: <repo>/tests/DfE.CheckPerformanceData.UnitTests/bin/{Config}/net10.0
        var tfmDir = unitBinDir.Name;
        var configDir = unitBinDir.Parent
            ?? throw NotResolvable(unitBinDir.FullName);
        var binDir = configDir.Parent
            ?? throw NotResolvable(unitBinDir.FullName);
        var unitProjDir = binDir.Parent
            ?? throw NotResolvable(unitBinDir.FullName);
        var testsDir = unitProjDir.Parent
            ?? throw NotResolvable(unitBinDir.FullName);

        var candidate = Path.Combine(
            testsDir.FullName,
            "DfE.CheckPerformanceData.IntegrationTests",
            "bin",
            configDir.Name,
            tfmDir,
            "DfE.CheckPerformanceData.IntegrationTests.dll");

        if (!File.Exists(candidate))
        {
            throw new XunitException(
                "PrdCaseCoverageTests could not resolve the IntegrationTests assembly. Tried:\n" +
                $"  1. Assembly.Load(\"DfE.CheckPerformanceData.IntegrationTests\") — FileNotFoundException\n" +
                $"  2. sibling bin path: {candidate} — not present\n" +
                "Landmine L-C fallback: move PrdCaseCoverageTests.cs to " +
                "tests/DfE.CheckPerformanceData.IntegrationTests/Search/ and add a project reference " +
                "from IntegrationTests -> UnitTests so typeof(SearchTermNormalizerTests).Assembly resolves.");
        }

        return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }

    private static XunitException NotResolvable(string binDir) => new(
        $"PrdCaseCoverageTests could not walk up from UnitTests bin dir '{binDir}' to locate the sibling " +
        "IntegrationTests project. Apply Landmine L-C fallback: move the file to IntegrationTests and add " +
        "a reverse project reference.");

    // Verbatim from tests/DfE.CheckPerformanceData.IntegrationTests/Architecture/AzureQueueCutoverGuardTests.cs.
    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    // Xunit.TraitAttribute (2.9.3) does not expose its ctor args as public properties, so
    // we read them via CustomAttributeData metadata rather than instantiating the attribute.
    private static IEnumerable<(string Name, string Value)> TraitPairs(Assembly asm) =>
        SafeGetTypes(asm)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(m => m.GetCustomAttributesData())
            .Where(d => d.AttributeType.FullName == TraitAttributeFullName
                        && d.ConstructorArguments.Count == 2
                        && d.ConstructorArguments[0].Value is string
                        && d.ConstructorArguments[1].Value is string)
            .Select(d => ((string)d.ConstructorArguments[0].Value!,
                          (string)d.ConstructorArguments[1].Value!));
}
