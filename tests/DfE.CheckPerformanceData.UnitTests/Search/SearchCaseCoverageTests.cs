using System.Reflection;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// SearchCaseCoverageTests reflection-walks both search test assemblies and asserts every
// documented search-behaviour case has at least one method-level
// [Trait("search-case", <slug>)] test. A companion fact does the same for the seven
// [Trait("search-filter", <slug>)] silent-filter invariants. Any future test deletion,
// rename, or trait removal that would silently drop a behaviour from coverage fails CI
// naming the missing slug(s).
//
// Search-behaviour case slug → intent map:
//   single-word          SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   multi-word-or        SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   explicit-or          SearchTermNormalizerTests
//   fuzz-safety          SearchFuzzInputTests (integration; curated hazards + deterministic random)
//   phrase               SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   negation             SearchTermNormalizerTests + PageNodeRepositorySearchTests
//   numeric-hyphenated   PageNodeRepositorySearchTests + ContentBlockRepositorySearchTests
//   operator-only        SearchFuzzInputTests OperatorOnly Theory subset (integration; operator-only inputs resolve to zero results without throwing)
//   pagination           SearchPaginationTests (integration) + SearchControllerPaginationTests (unit); page/pageSize routing and silent clamps
//   very-short           SiteSearchServiceTests + ContentBlockSearchServiceTests
//   long-query           SearchControllerTests
//   empty-or-whitespace  SearchTermNormalizerTests + SiteSearchMergedPagedTests + ContentBlockSearchServiceTests
//   special-chars        SearchTermNormalizerTests + SearchSnippetTests + ContentBlockRepositorySearchTests + ContentBlockSearchServiceTests
//   duplicate-blocks     ContentBlockRepositorySearchTests
//   editor-suppressed    ContentBlockRepositorySearchTests + SiteSearchServiceFilterTests + ContentBlockSearchServiceTests
//   keywords-boost       PageNodeRepositorySearchTests + SiteSearchServiceRankingTests
//   unpublished-target   PageNodeRepositorySearchTests + SiteSearchServiceFilterTests + ContentBlockSearchServiceTests
//   scope-filter         SiteSearchServiceTests
//   invalid-query        SiteSearchMergedPagedTests
//
// DB-unavailable behaviour (search should return a graceful 503 rather than throw) is
// deliberately excluded — that behaviour is covered separately by
// tests/DfE.CheckPerformanceData.IntegrationTests/Search/SearchResilienceTests.cs.
//
// Attributes are enumerated at the method level only via
// GetMethods(...).GetCustomAttributesData(). Class-level [Trait] attributes are invisible
// to this walk. Every relevant [Fact] / [Theory] must carry the trait on the method
// itself. Reading is via CustomAttributeData (not GetCustomAttributes<TraitAttribute>())
// because Xunit.TraitAttribute (2.9.3) does not expose Name/Value as public members —
// the ctor args are consumed only by the TraitDiscoverer at test-discovery time.
public sealed class SearchCaseCoverageTests
{
    private const string TraitAttributeFullName = "Xunit.TraitAttribute";

    // The set of documented search-behaviour cases; each must have ≥ 1 traited test.
    private static readonly HashSet<string> ExpectedCases =
        new(StringComparer.Ordinal)
        {
            "single-word",
            "multi-word-or",
            "explicit-or",
            "fuzz-safety",
            "phrase",
            "negation",
            "numeric-hyphenated",
            "operator-only",
            "pagination",
            "very-short",
            "long-query",
            "empty-or-whitespace",
            "special-chars",
            "duplicate-blocks",
            "editor-suppressed",
            "keywords-boost",
            "unpublished-target",
            "scope-filter",
            "invalid-query",
            "zero-result-telemetry",
            "rank-breakdown-telemetry",
        };

    // Silent-filter slugs. Each must have ≥1 [Trait("search-filter", <slug>)] test
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
    public void EverySearchCase_HasAtLeastOneTraitedTest()
    {
        var observed = SearchTestAssemblies()
            .SelectMany(asm => TraitPairs(asm))
            .Where(t => t.Name == "search-case")
            .Select(t => t.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedCases.Except(observed).ToList();
        Assert.True(missing.Count == 0,
            $"Search-behaviour cases without any [Trait(\"search-case\", \"<slug>\")] test: {string.Join(", ", missing)}. " +
            "The DB-unavailable case is deliberately excluded — covered separately by resilience tests.");
    }

    [Fact]
    public void EverySilentFilter_HasAtLeastOneTraitedTest()
    {
        var observed = SearchTestAssemblies()
            .SelectMany(asm => TraitPairs(asm))
            .Where(t => t.Name == "search-filter")
            .Select(t => t.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedFilters.Except(observed).ToList();
        Assert.True(missing.Count == 0,
            $"Silent filters without any [Trait(\"search-filter\", \"<slug>\")] test: {string.Join(", ", missing)}.");
    }

    private static IEnumerable<Assembly> SearchTestAssemblies() =>
    [
        typeof(SearchTermNormalizerTests).Assembly, // UnitTests (this project)
        LoadIntegrationTestsAssembly(),
    ];

    // The IntegrationTests DLL is not a project reference of UnitTests, so it does not
    // ship into the UnitTests test-host bin folder. Plain Assembly.Load(name) will throw
    // FileNotFoundException. Locate the sibling bin manually and load from disk.
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

        var unitBinDir = new FileInfo(typeof(SearchCaseCoverageTests).Assembly.Location).Directory
            ?? throw new XunitException(
                "SearchCaseCoverageTests: could not determine UnitTests bin directory from Assembly.Location.");

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
                "SearchCaseCoverageTests could not resolve the IntegrationTests assembly. Tried:\n" +
                $"  1. Assembly.Load(\"DfE.CheckPerformanceData.IntegrationTests\") — FileNotFoundException\n" +
                $"  2. sibling bin path: {candidate} — not present\n" +
                "Fallback: move SearchCaseCoverageTests.cs to " +
                "tests/DfE.CheckPerformanceData.IntegrationTests/Search/ and add a project reference " +
                "from IntegrationTests -> UnitTests so typeof(SearchTermNormalizerTests).Assembly resolves.");
        }

        return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }

    private static XunitException NotResolvable(string binDir) => new(
        $"SearchCaseCoverageTests could not walk up from UnitTests bin dir '{binDir}' to locate the sibling " +
        "IntegrationTests project. Fallback: move the file to IntegrationTests and add a reverse project reference.");

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
