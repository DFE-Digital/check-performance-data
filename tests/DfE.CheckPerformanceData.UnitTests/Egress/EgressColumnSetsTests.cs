using DfE.CheckPerformanceData.Application.Egress;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// Headings are a contract with LDS: exact text, order and count (AB#292610 "Field headings match
// the spec exactly"). The Remove set is the spike's list verbatim; the New learners set is DERIVED
// from the tactical Zendesk report and AB#292610's rules and is FLAGGED for verification against
// LDS_CYPMD_Data specification v2.4 — when the spec arrives, edit EgressColumnSets and these
// expectations together, nothing else.
public sealed class EgressColumnSetsTests
{
    [Fact]
    public void Remove_learners_headings_are_the_spike_list_in_order()
    {
        var headers = EgressColumnSets.RemoveLearners.Select(c => c.Header).ToArray();
        Assert.Equal(
            ["Correction_ID", "Correction_Type", "Correction_Reason", "Key_Stage", "Establishment_Number",
             "Surname", "Forename", "Sex", "Date_of_Birth", "Cycle_Year", "Cycle_Month", "Local_Authority", "Learner_ID"],
            headers);
    }

    [Fact]
    public void New_learners_headings_put_UPN_between_ULN_and_the_matched_LDS_ref()
    {
        var headers = EgressColumnSets.NewLearners.Select(c => c.Header).ToArray();
        Assert.Equal(
            ["Correction_ID", "Correction_Type", "Key_Stage", "Local_Authority", "Establishment_Number",
             "Surname", "Middle_Name", "Forename", "Sex", "Date_of_Birth", "Admission_Date", "Postcode",
             "Cycle_Year", "Cycle_Month", "School_URN", "ULN", "UPN", "Learner_ID", "Year_Group", "SEN_Status"],
            headers);
        Assert.Equal(Array.IndexOf(headers, "ULN") + 1, Array.IndexOf(headers, "UPN"));
        Assert.Equal(Array.IndexOf(headers, "UPN") + 1, Array.IndexOf(headers, "Learner_ID"));
    }

    [Fact]
    public void No_heading_carries_a_trailing_underscore_or_whitespace()
    {
        foreach (var header in EgressColumnSets.RemoveLearners.Select(c => c.Header)
                     .Concat(EgressColumnSets.NewLearners.Select(c => c.Header)))
        {
            Assert.Equal(header.Trim(), header);
            Assert.False(header.EndsWith('_'), header);
        }
    }

    [Fact]
    public void Remove_row_projects_every_column_from_the_row()
    {
        var row = SampleRows.Remove();
        var values = EgressColumnSets.RemoveLearners.Select(c => c.Value(row)).ToArray();
        Assert.Equal(["88856", "31", "4", "KS4", "4603", "Bellingham", "Jude", "M", "2007-06-01", "2026", "6", "873", "10000011"], values);
    }

    [Fact]
    public void New_row_projects_every_column_from_the_row()
    {
        var row = SampleRows.New();
        var values = EgressColumnSets.NewLearners.Select(c => c.Value(row)).ToArray();
        Assert.Equal(["69390", "10", "KS4", "881", "5412", "Lennox", "", "Annie", "F", "2010-09-07", "2018-09-04", "",
                      "2026", "6", "136412", "", "A881541200011", "", "10", "N"], values);
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
        Surname: "Lennox", MiddleName: "", Forename: "Annie", Sex: "F", DateOfBirth: "2010-09-07", AdmissionDate: "2018-09-04",
        Postcode: "", CycleYear: "2026", CycleMonth: "6", SchoolUrn: "136412", Uln: "", Upn: "A881541200011", LearnerId: "",
        YearGroup: "10", SenStatus: "N",
        ChangeRequestId: Guid.Parse("22222222-2222-2222-2222-222222222222"), TicketId: 69390, ReferenceNumber: "CYPMD_KS4June_AAA0002");
}
