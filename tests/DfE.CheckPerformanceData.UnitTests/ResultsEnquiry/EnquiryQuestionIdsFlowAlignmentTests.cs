using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

/// <summary>
/// #712: the two grade question ids are owned by <see cref="EnquiryQuestionIds"/> and are read
/// from the saved journey record by the Results Enquiries tab. These pin the constants to the
/// shipped flow files: a rename of a constant must fail here before the tab can start showing
/// empty grades.
/// </summary>
public sealed class EnquiryQuestionIdsFlowAlignmentTests
{
    // Mirrors FileSystemQuestionFlowClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static QuestionFlowConfig Load(string fileName)
        => JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(LocateFlowFile(fileName)), JsonOptions)!;

    private static IEnumerable<string> QuestionIds(QuestionFlowConfig flow) =>
        flow.Pages.SelectMany(p => p.Questions).Select(q => q.Id);

    [Fact]
    public void The_revised_grade_id_is_a_question_in_the_incorrect_grade_flow()
    {
        var ids = QuestionIds(Load("IncorrectGrade_Post16.json"));
        Assert.Contains(EnquiryQuestionIds.RevisedGrade, ids);
    }

    [Fact]
    public void The_missing_grade_id_is_a_question_in_the_missing_qualification_flow()
    {
        var ids = QuestionIds(Load("MissingQualification_Post16.json"));
        Assert.Contains(EnquiryQuestionIds.MissingGrade, ids);
    }

    private static string LocateFlowFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate " + fileName + " from " + AppContext.BaseDirectory);
    }
}
