using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Issue 517 (AB#304143): the KS4 June Remove evidence question "How does the evidence demonstrate
// why the pupil should be removed?" had grey hint text below it ("Your request will not be accepted
// unless you provide the correct evidence"). The bold question is enough on its own, so the hint is
// removed from every evidence page in the flow. The 16-19 Remove flow had the same hint on its
// student evidence question, so it is removed there too.
public class RemoveFlowEvidenceQuestionHintTests
{
    private const string EvidenceQuestionId = "how-evidence-supports";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("Remove_KS4June.json")]
    [InlineData("Remove_Post16.json")]
    public void Evidence_question_has_no_hint(string flowFile)
    {
        var config = JsonSerializer.Deserialize<QuestionFlowConfig>(
            File.ReadAllText(Path.Combine(LocateFlowsDirectory(), flowFile)), JsonOptions)!;

        var evidenceQuestions = config.Pages
            .SelectMany(p => p.Questions)
            .Where(q => q.Id == EvidenceQuestionId)
            .ToList();

        Assert.NotEmpty(evidenceQuestions);
        Assert.All(evidenceQuestions, q => Assert.Null(q.Hint));
    }

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
}
