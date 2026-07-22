using System.Reflection;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Contract test for Application/Search/SearchWeights.cs. Locks three things:
//   1. Five `public const char` members are present with the ranking letters
//      declared by the ranking contract (Keywords A, Title B, Subtitle C,
//      Body D, Value B).
//   2. Every constant carries an XML `<summary>` doc block citing the ranking
//      contract's weight table (verified by source-file scan because
//      `<GenerateDocumentationFile>false</GenerateDocumentationFile>` in the
//      UnitTests csproj means XML metadata is unreachable via reflection).
//   3. The `setweight(..., 'X')` letters baked into the three EF Core entity
//      configurations under Persistence/Configurations/ match the constants.
//      Deleting a constant, flipping a letter, or editing one place without
//      the other fails a test here.
public sealed class SearchWeightsTests
{
    private static string ReadPersistenceConfig(string fileName)
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Persistence", "Configurations"));
        return File.ReadAllText(Path.Combine(dir, fileName));
    }

    private static string ReadApplicationSource(string subFolder, string fileName)
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Application", subFolder));
        return File.ReadAllText(Path.Combine(dir, fileName));
    }

    // Extracts the letter from every `, 'X')` at the tail of a setweight(...) call.
    // The outer setweight argument list nests parentheses and commas (setweight wraps
    // to_tsvector(..., coalesce("...", '')), 'X')) so we can't cleanly match the whole
    // call in one regex — but the closing `, 'X')` pattern is narrow enough to be safe
    // as a standalone anchor across all three configuration files.
    private static IEnumerable<char> ExtractSetweightLetters(string source)
    {
        var matches = Regex.Matches(source, @",\s*'([A-D])'\)");
        foreach (Match m in matches)
        {
            yield return m.Groups[1].Value[0];
        }
    }

    [Theory]
    [InlineData("KeywordsWeight", 'A')]
    [InlineData("TitleWeight", 'B')]
    [InlineData("SubtitleWeight", 'C')]
    [InlineData("BodyWeight", 'D')]
    [InlineData("ValueWeight", 'B')]
    public void SearchWeights_ExposesNamedConstant_WithExpectedLetter(string constantName, char expectedLetter)
    {
        var field = typeof(SearchWeights).GetField(
            constantName,
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        var actual = field!.GetRawConstantValue();
        Assert.Equal(expectedLetter, actual);
    }

    [Fact]
    public void SearchWeights_EachConstant_HasXmlSummaryCitingPrdSection_7_1()
    {
        var source = ReadApplicationSource("Search", "SearchWeights.cs");

        string[] constants =
        {
            "KeywordsWeight",
            "TitleWeight",
            "SubtitleWeight",
            "BodyWeight",
            "ValueWeight",
        };

        foreach (var name in constants)
        {
            var declPattern = new Regex(@"public\s+const\s+char\s+" + Regex.Escape(name) + @"\b");
            var declMatch = declPattern.Match(source);
            Assert.True(
                declMatch.Success,
                $"Expected `public const char {name}` declaration in SearchWeights.cs.");

            // Look at the ~500 characters immediately preceding the declaration and take the
            // LAST <summary>...</summary> block from that window. That's the doc block "attached"
            // to the constant (the file-level / class-level summary sits further back and is
            // excluded by taking the last match).
            var windowStart = Math.Max(0, declMatch.Index - 500);
            var precedingWindow = source[windowStart..declMatch.Index];

            var summaryMatches = Regex.Matches(precedingWindow, @"<summary>([\s\S]*?)</summary>");
            Assert.True(
                summaryMatches.Count > 0,
                $"Expected an XML `<summary>` doc block preceding `{name}` in SearchWeights.cs.");

            var lastSummary = summaryMatches[^1].Groups[1].Value;
            var citesPrd = lastSummary.Contains("PRD §7.1") || lastSummary.Contains("PRD 7.1");
            Assert.True(
                citesPrd,
                $"Expected the `<summary>` doc block for `{name}` to cite `PRD §7.1` "
                + $"(or ASCII fallback `PRD 7.1`). Last summary body was:{Environment.NewLine}{lastSummary}");
        }
    }

    [Fact]
    public void PageNodeConfiguration_SetweightLetters_MatchSearchWeightsConstants()
    {
        var source = ReadPersistenceConfig("PageNodeConfiguration.cs");
        var letters = ExtractSetweightLetters(source).ToHashSet();

        Assert.Contains(SearchWeights.KeywordsWeight, letters);
        Assert.Contains(SearchWeights.TitleWeight, letters);
        Assert.Contains(SearchWeights.SubtitleWeight, letters);
    }

    [Fact]
    public void PageNodeVersionConfiguration_SetweightLetters_MatchSearchWeightsConstants()
    {
        var source = ReadPersistenceConfig("PageNodeVersionConfiguration.cs");
        var letters = ExtractSetweightLetters(source).ToHashSet();

        Assert.Contains(SearchWeights.BodyWeight, letters);
    }

    [Fact]
    public void ContentBlockConfiguration_SetweightLetters_MatchSearchWeightsConstants()
    {
        var source = ReadPersistenceConfig("ContentBlockConfiguration.cs");
        var letters = ExtractSetweightLetters(source).ToHashSet();

        Assert.Contains(SearchWeights.KeywordsWeight, letters);
        Assert.Contains(SearchWeights.ValueWeight, letters);
    }
}
