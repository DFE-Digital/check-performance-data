using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// Headings are a contract with LDS: exact text, order and count (AB#292610 "Field headings match
// the spec exactly"). Both sets are the LDS_CYPMD_Data specification v2.4 sheets ("New Learner",
// "Remove Learner") read top to bottom, keeping only rows marked X for the key stage in question.
// Middle_Name is struck through in v2.4 ("CYPMD will not be sending this field from June 2026")
// and is therefore absent. Edit EgressColumnSets and these expectations together, nothing else.
public sealed class EgressColumnSetsTests
{
    private static readonly string[] RemoveBase =
    [
        "Correction_ID", "Correction_Type", "Correction_Reason", "Key_Stage", "Establishment_Number",
        "Surname", "Forename", "Sex", "Date_of_Birth", "Cycle_Year", "Cycle_Month", "Local_Authority", "Learner_ID"
    ];

    private static readonly string[] NewBase =
    [
        "Correction_ID", "Correction_Type", "Key_Stage", "Establishment_Number", "Surname", "Forename", "Sex",
        "Date_of_Birth", "Admission_Date", "Post_Code", "Cycle_Year", "Cycle_Month", "Local_Authority", "URN",
        "ULN", "UPN", "Learner_ID", "Year_Group"
    ];

    [Fact]
    public void Remove_learners_base_headings_are_the_spec_sheet_in_order()
        => Assert.Equal(RemoveBase, EgressColumnSets.RemoveLearners.Select(c => c.Header).ToArray());

    [Fact]
    public void New_learners_base_headings_are_the_spec_sheet_in_order_without_the_struck_Middle_Name()
    {
        var headers = EgressColumnSets.NewLearners.Select(c => c.Header).ToArray();
        Assert.Equal(NewBase, headers);
        Assert.Equal(Array.IndexOf(headers, "ULN") + 1, Array.IndexOf(headers, "UPN"));
        Assert.Equal(Array.IndexOf(headers, "UPN") + 1, Array.IndexOf(headers, "Learner_ID"));
        Assert.DoesNotContain("Middle_Name", headers);
        Assert.DoesNotContain("SEN_Status", headers);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS2)]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void New_learners_for_KS2_and_KS4_is_the_base_set(CheckingWindowType windowType)
        => Assert.Equal(NewBase, EgressColumnSets.NewLearnersFor(windowType).Select(c => c.Header).ToArray());

    [Fact]
    public void New_learners_for_16_19_appends_the_attendance_years_and_KS4_year()
    {
        var headers = EgressColumnSets.NewLearnersFor(CheckingWindowType.Post16).Select(c => c.Header).ToArray();
        Assert.Equal([.. NewBase, "Attendance_Year_0", "Attendance_Year_1", "Attendance_Year_2", "KS4_Year"], headers);
        // No Post16 Add journey exists yet, so the four extra cells are blank (spec: NULL allowed).
        var values = EgressColumnSets.NewLearnersFor(CheckingWindowType.Post16).Select(c => c.Value(SampleRows.New())).ToArray();
        Assert.Equal(["", "", "", ""], values[^4..]);
    }

    // v2.4 "Remove Learner": Year_Group is X for KS4 only ("for year group change requests only");
    // Removal_Year_0..2 are X for 16-18 only; KS2 gets neither.
    [Fact]
    public void Remove_learners_for_KS2_is_the_base_set()
        => Assert.Equal(RemoveBase, EgressColumnSets.RemoveLearnersFor(CheckingWindowType.KS2).Select(c => c.Header).ToArray());

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    public void Remove_learners_for_KS4_appends_Year_Group(CheckingWindowType windowType)
    {
        var columns = EgressColumnSets.RemoveLearnersFor(windowType);
        Assert.Equal([.. RemoveBase, "Year_Group"], columns.Select(c => c.Header).ToArray());
        Assert.Equal("12", columns[^1].Value(SampleRows.Remove() with { YearGroup = "12" }));
    }

    [Fact]
    public void Remove_learners_for_16_19_appends_the_three_removal_years()
    {
        var columns = EgressColumnSets.RemoveLearnersFor(CheckingWindowType.Post16);
        Assert.Equal([.. RemoveBase, "Removal_Year_0", "Removal_Year_1", "Removal_Year_2"], columns.Select(c => c.Header).ToArray());
        var row = SampleRows.Remove() with { RemovalYear0 = "TRUE", RemovalYear1 = "FALSE", RemovalYear2 = "TRUE" };
        Assert.Equal(["TRUE", "FALSE", "TRUE"], columns.Skip(RemoveBase.Length).Select(c => c.Value(row)).ToArray());
    }

    [Fact]
    public void No_heading_carries_a_trailing_underscore_or_whitespace()
    {
        foreach (var header in EgressColumnSets.RemoveLearners.Select(c => c.Header)
                     .Concat(EgressColumnSets.NewLearnersFor(CheckingWindowType.Post16).Select(c => c.Header)))
        {
            Assert.Equal(header.Trim(), header);
            Assert.False(header.EndsWith('_'), header);
        }
    }

    [Fact]
    public void Remove_row_projects_every_column_from_the_row()
    {
        var values = EgressColumnSets.RemoveLearners.Select(c => c.Value(SampleRows.Remove())).ToArray();
        Assert.Equal(["88856", "31", "4", "KS4", "4603", "Bellingham", "Jude", "M", "2007-06-01", "2026", "6", "873", "10000011"], values);
    }

    [Fact]
    public void New_row_projects_every_column_from_the_row_in_spec_order()
    {
        var values = EgressColumnSets.NewLearners.Select(c => c.Value(SampleRows.New())).ToArray();
        Assert.Equal(["69390", "10", "KS4", "5412", "Lennox", "Annie", "F", "2010-09-07", "2018-09-04", "",
                      "2026", "6", "881", "136412", "", "A881541200011", "", "10"], values);
    }
}

internal static class SampleRows
{
    public static RemoveLearnerRow Remove() => new(
        CorrectionId: "88856", CorrectionType: "31", CorrectionReason: "4", KeyStage: "KS4",
        EstablishmentNumber: "4603", Surname: "Bellingham", Forename: "Jude", Sex: "M", DateOfBirth: "2007-06-01",
        CycleYear: "2026", CycleMonth: "6", LocalAuthority: "873", LearnerId: "10000011",
        ChangeRequestId: Guid.Parse("11111111-1111-1111-1111-111111111111"), TicketId: 88856, ReferenceNumber: "CYPMD_KS4June_AAA0001");

    public static NewLearnerRow New() => new(
        CorrectionId: "69390", CorrectionType: "10", KeyStage: "KS4", LocalAuthority: "881", EstablishmentNumber: "5412",
        Surname: "Lennox", Forename: "Annie", Sex: "F", DateOfBirth: "2010-09-07", AdmissionDate: "2018-09-04",
        Postcode: "", CycleYear: "2026", CycleMonth: "6", SchoolUrn: "136412", Uln: "", Upn: "A881541200011", LearnerId: "",
        YearGroup: "10",
        ChangeRequestId: Guid.Parse("22222222-2222-2222-2222-222222222222"), TicketId: 69390, ReferenceNumber: "CYPMD_KS4June_AAA0002");
}
