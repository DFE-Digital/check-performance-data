using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public sealed class HeldResultsTests
{
    private static ResultRecord Result(string qan, string session, string source, string grade) =>
        new() { CypmdId = "500002", Qan = qan, Session = session, Grade = grade, SourceFile = source };

    private static readonly ResultRecord Included = Result("60172186", "S2024", ResultsFileTags.Post16Included, "M");
    private static readonly ResultRecord Late1 = Result("60172186", "S2024", ResultsFileTags.Post16LateResults1, "D");
    private static readonly ResultRecord Late2 = Result(" 60172186 ", "s2024", ResultsFileTags.Post16LateResults2, "P");
    private static readonly ResultRecord OtherSession = Result("60172186", "S2023", ResultsFileTags.Post16Included, "D");
    private static readonly ResultRecord OtherQualification = Result("60146084", "S2024", ResultsFileTags.Post16Included, "D");

    [Fact]
    public void The_other_files_copies_of_the_selected_result_are_returned_in_order() =>
        Assert.Equal(
            [Late1, Late2],
            HeldResults.InOtherFiles(Included, [Included, OtherSession, Late1, OtherQualification, Late2]));

    [Fact]
    public void The_selected_result_is_not_its_own_copy() =>
        Assert.Empty(HeldResults.InOtherFiles(Included, [Included]));

    [Fact]
    public void Another_session_or_qualification_is_not_a_copy() =>
        Assert.Empty(HeldResults.InOtherFiles(Included, [Included, OtherSession, OtherQualification]));
}
