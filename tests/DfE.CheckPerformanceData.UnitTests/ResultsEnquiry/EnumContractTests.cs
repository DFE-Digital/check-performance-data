using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#296648: results-enquiry enum members must exist, serialize within the
// ChangeRequest string-column limits (max 20), and be appended after existing
// members so stored values are unmoved.
public sealed class EnumContractTests
{
    [Fact]
    public void ResultsEnquiry_request_type_exists_and_fits_column()
    {
        var name = RequestType.ResultsEnquiry.ToString();
        Assert.Equal("ResultsEnquiry", name);
        Assert.True(name.Length <= 20);
        Assert.Equal(2, (int)RequestType.ResultsEnquiry); // appended after ConfirmCorrect
    }

    [Fact]
    public void IncorrectGrade_what_to_change_exists_and_fits_column()
    {
        var name = WhatToChange.IncorrectGrade.ToString();
        Assert.Equal("IncorrectGrade", name);
        Assert.True(name.Length <= 20);
        Assert.Equal(4, (int)WhatToChange.IncorrectGrade); // appended after Add
    }

    [Fact]
    public void Journey_engine_enums_gain_results_enquiry_members()
    {
        Assert.Equal(4, (int)PageType.ResultSearch);
        Assert.Equal(5, (int)PageType.ResultDetails);
        Assert.Equal(6, (int)QuestionType.GradeSelect);
        Assert.Equal(2, (int)NextSteps.ResultsEnquiry); // appended after Confirm
    }

    [Fact]
    public void MissingQualification_what_to_change_exists_and_fits_column()
    {
        // ChangeRequestConfiguration stores AmendmentType as a string with HasMaxLength(20), and
        // "MissingQualification" is exactly 20 characters — a rename that grows it fails here rather
        // than truncating in Npgsql on first submission. The name is asserted exactly, not just
        // measured: it IS the persisted value, so a shorter rename would orphan every stored row.
        var name = WhatToChange.MissingQualification.ToString();
        Assert.Equal("MissingQualification", name);
        Assert.Equal(20, name.Length);
        Assert.Equal(5, (int)WhatToChange.MissingQualification); // appended after IncorrectGrade
    }

    [Fact]
    public void Journey_engine_enums_gain_missing_qualification_members()
    {
        // The ordinals are the contract: PageType and QuestionType are serialized into the flow JSON
        // by NAME, but RequestState blobs and any numeric round-trip depend on position. Inserting a
        // member above these silently repoints every stored value — which is what the append-only
        // rule exists to prevent, and what the previous length-only pin left unguarded.
        Assert.Equal(6, (int)PageType.QualificationSearch);
        Assert.Equal(7, (int)PageType.QualificationDetails);
        Assert.Equal(7, (int)QuestionType.SyllabusSelect);
    }

    [Fact]
    public void ResultDoesNotBelong_what_to_change_exists_and_fits_column()
    {
        // Same contract as MissingQualification above: the name IS the persisted AmendmentType value
        // (varchar(20)), so it is asserted exactly, and the ordinal is pinned because RequestState
        // blobs round-trip numerically.
        var name = WhatToChange.ResultDoesNotBelong.ToString();
        Assert.Equal("ResultDoesNotBelong", name);
        Assert.True(name.Length <= 20);
        Assert.Equal(6, (int)WhatToChange.ResultDoesNotBelong); // appended after MissingQualification
    }
}
