using DfE.CheckPerformanceData.Application.Egress;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// "All values in all fields are permitted values as defined by the spec" (AB#292610). One failure
// per offending field, naming the field, so the ops user can correct the source and re-run.
public sealed class LdsSpecValidatorTests
{
    [Fact]
    public void A_complete_remove_row_passes()
        => Assert.Empty(LdsSpecValidator.Validate(SampleRows.Remove()));

    [Fact]
    public void A_complete_new_learner_row_passes()
        => Assert.Empty(LdsSpecValidator.Validate(SampleRows.New()));

    [Theory]
    [InlineData("Local_Authority", "87")]
    [InlineData("Local_Authority", "")]
    [InlineData("Establishment_Number", "460")]
    [InlineData("Sex", "X")]
    [InlineData("Sex", "")]
    [InlineData("Date_of_Birth", "01/06/2007")]
    [InlineData("Correction_Reason", "")]
    [InlineData("Key_Stage", "KS3")]
    [InlineData("Cycle_Month", "13")]
    [InlineData("Learner_ID", "")]
    [InlineData("Surname", "")]
    public void A_remove_row_fails_on_the_named_field(string field, string bad)
    {
        var row = SampleRows.Remove();
        row = field switch
        {
            "Local_Authority" => row with { LocalAuthority = bad },
            "Establishment_Number" => row with { EstablishmentNumber = bad },
            "Sex" => row with { Sex = bad },
            "Date_of_Birth" => row with { DateOfBirth = bad },
            "Correction_Reason" => row with { CorrectionReason = bad },
            "Key_Stage" => row with { KeyStage = bad },
            "Cycle_Month" => row with { CycleMonth = bad },
            "Learner_ID" => row with { LearnerId = bad },
            "Surname" => row with { Surname = bad },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var failures = LdsSpecValidator.Validate(row);

        var failure = Assert.Single(failures);
        Assert.Equal(field, failure.Field);
        Assert.Equal("Validate against LDS spec", failure.Step);
        Assert.Equal(88856, failure.TicketId);
    }

    [Theory]
    [InlineData("Sex", "Z")]
    [InlineData("Admission_Date", "")]
    [InlineData("Year_Group", "7")]
    [InlineData("UPN", "A8815412000110000")]
    [InlineData("URN", "")]
    public void A_new_learner_row_fails_on_the_named_field(string field, string bad)
    {
        var row = SampleRows.New();
        row = field switch
        {
            "Sex" => row with { Sex = bad },
            "Admission_Date" => row with { AdmissionDate = bad },
            "Year_Group" => row with { YearGroup = bad },
            "UPN" => row with { Upn = bad },
            "URN" => row with { SchoolUrn = bad },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var failure = Assert.Single(LdsSpecValidator.Validate(row));
        Assert.Equal(field, failure.Field);
    }

    [Fact]
    public void New_learner_optional_fields_may_be_blank()
    {
        var row = SampleRows.New() with { Upn = "", Uln = "", LearnerId = "", Postcode = "" };
        Assert.Empty(LdsSpecValidator.Validate(row));
    }

    [Fact]
    public void Sex_U_is_permitted_for_a_new_learner_but_not_a_removal()
    {
        Assert.Empty(LdsSpecValidator.Validate(SampleRows.New() with { Sex = "U" }));
        Assert.Single(LdsSpecValidator.Validate(SampleRows.Remove() with { Sex = "U" }));
    }
}
