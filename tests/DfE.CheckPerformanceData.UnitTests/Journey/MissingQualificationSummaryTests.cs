using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class MissingQualificationSummaryTests
{
    private static MissingQualificationSummary Summary(bool isCohortWide) => new()
    {
        DfeNumber = "860/4070",
        KeyStageLabel = "16 to 19",
        StudentName = "Billy B",
        IsCohortWide = isCohortWide,
        CohortCount = isCohortWide ? "10" : null,
        CypmdId = "500001",
        AwardingOrganisation = "AQA",
        Qan = "60146084",
        QualificationTitle = "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
        SyllabusCode = "8300H",
        AwardDate = "1 June 2025",
        Ncn = "12345",
        GradeAchieved = "2",
        AdditionalInformation = "Some extra context",
        QualificationPageId = "select-qualification",
        DetailsPageId = "qualification-details",
        AdditionalInformationPageId = "additional-info"
    };

    [Fact]
    public void The_single_student_rows_run_in_the_figma_order()
    {
        var lines = Summary(isCohortWide: false).Lines;
        Assert.Equal(new[] { "DfE number", "Key stage", "Enquiry type", "Name of student", "CYPMD ID",
            "Awarding Organisation (AO) name", "Qualification number (QAN)", "Qualification name and subject",
            "Syllabus code", "Award date", "NCN", "Grade achieved", "Additional information" },
            lines.Select(l => l.Key).ToArray());
    }

    [Fact]
    public void The_cohort_branch_adds_the_count_row_and_relabels_the_student()
    {
        var lines = Summary(isCohortWide: true).Lines;
        Assert.Contains(lines, l => l.Key == "Number of students in affected cohort" && l.Value == "10");
        Assert.Contains(lines, l => l.Key == "Name of a student in cohort");
        Assert.DoesNotContain(lines, l => l.Key == "Name of student");
    }

    [Fact]
    public void Only_the_user_editable_rows_carry_change_links()
    {
        // Enquiry type, student identity and the derived qualification title have no Change link;
        // AO and QAN change through the qualification page, everything else through its own page.
        var byKey = Summary(false).Lines.ToDictionary(l => l.Key);
        Assert.False(byKey["Enquiry type"].HasChange);
        Assert.False(byKey["Qualification name and subject"].HasChange);
        Assert.Equal("select-qualification", byKey["Awarding Organisation (AO) name"].ChangePageId);
        Assert.Equal("select-qualification", byKey["Qualification number (QAN)"].ChangePageId);
        Assert.Equal("qualification-details", byKey["Syllabus code"].ChangePageId);
        Assert.Equal("qualification-details", byKey["Grade achieved"].ChangePageId);
        Assert.Equal("additional-info", byKey["Additional information"].ChangePageId);
    }

    [Fact]
    public void Enquiry_type_reads_Missing_qualification()
        => Assert.Equal("Missing qualification", Summary(false).Lines.Single(l => l.Key == "Enquiry type").Value);
}
