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

    // Review finding (18 Sep): the file goes to an external organisation and may be opened in a
    // spreadsheet, where a cell beginning with = + - @ is evaluated as a formula (OWASP CSV
    // injection). Surname and Forename are the only free-text cells — typed by the school in the
    // Add journey. Rejecting here keeps the file spec-pure (no apostrophe prefix LDS never asked
    // for) and fails the record with a named reason instead of silently rewriting a name.
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd")]
    [InlineData("-2")]
    [InlineData("@SUM(A1)")]
    public void A_new_learner_name_beginning_with_a_formula_trigger_fails_that_field(string bad)
    {
        var failures = LdsSpecValidator.Validate(SampleRows.New() with { Forename = bad });

        var failure = Assert.Single(failures);
        Assert.Equal("Forename", failure.Field);
        Assert.Equal("must not begin with =, +, - or @", failure.Reason);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("-Smith")]
    public void A_remove_row_surname_beginning_with_a_formula_trigger_fails_that_field(string bad)
    {
        var failures = LdsSpecValidator.Validate(SampleRows.Remove() with { Surname = bad });

        var failure = Assert.Single(failures);
        Assert.Equal("Surname", failure.Field);
        Assert.Equal("must not begin with =, +, - or @", failure.Reason);
    }

    // A hyphen INSIDE a name is ordinary (double-barrelled surnames); only a leading trigger is refused.
    [Fact]
    public void A_hyphenated_surname_passes()
        => Assert.Empty(LdsSpecValidator.Validate(SampleRows.Remove() with { Surname = "Smith-Jones" }));

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
    [InlineData("Year_Group", "14")]
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

    // v2.4: both sheets permit F, M and U ("can allow 'U' (unknown)").
    [Fact]
    public void Sex_U_is_permitted_on_both_files()
    {
        Assert.Empty(LdsSpecValidator.Validate(SampleRows.New() with { Sex = "U" }));
        Assert.Empty(LdsSpecValidator.Validate(SampleRows.Remove() with { Sex = "U" }));
    }

    // v2.4 New Learner: Year_Group is 1-13 and NULL-able; ULN is varchar(11); Post_Code varchar(8).
    [Theory]
    [InlineData("1")]
    [InlineData("13")]
    [InlineData("")]
    public void New_learner_year_group_may_be_blank_or_any_year_1_to_13(string yearGroup)
        => Assert.Empty(LdsSpecValidator.Validate(SampleRows.New() with { YearGroup = yearGroup }));

    [Theory]
    [InlineData("Year_Group", "0")]
    [InlineData("Year_Group", "14")]
    [InlineData("Year_Group", "ten")]
    [InlineData("ULN", "123456789012")]
    [InlineData("ULN", "12345678A")]
    [InlineData("Post_Code", "SW1A 1AA extra")]
    public void New_learner_out_of_range_values_fail_on_the_named_field(string field, string bad)
    {
        var row = field switch
        {
            "Year_Group" => SampleRows.New() with { YearGroup = bad },
            "ULN" => SampleRows.New() with { Uln = bad },
            "Post_Code" => SampleRows.New() with { Postcode = bad },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var failure = Assert.Single(LdsSpecValidator.Validate(row));
        Assert.Equal(field, failure.Field);
    }

    [Fact]
    public void New_learner_eleven_digit_ULN_and_eight_character_postcode_pass()
        => Assert.Empty(LdsSpecValidator.Validate(SampleRows.New() with { Uln = "12345678901", Postcode = "SW1A 1AA" }));

    [Theory]
    [InlineData("Year_Group", "14")]
    [InlineData("Removal_Year_0", "yes")]
    [InlineData("Removal_Year_2", "true")]   // spec value is upper-case TRUE/FALSE
    public void Remove_row_key_stage_specific_values_fail_on_the_named_field(string field, string bad)
    {
        var row = field switch
        {
            "Year_Group" => SampleRows.Remove() with { YearGroup = bad },
            "Removal_Year_0" => SampleRows.Remove() with { RemovalYear0 = bad },
            "Removal_Year_2" => SampleRows.Remove() with { RemovalYear2 = bad },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var failure = Assert.Single(LdsSpecValidator.Validate(row));
        Assert.Equal(field, failure.Field);
    }

    [Fact]
    public void Remove_row_key_stage_specific_values_may_be_blank_or_valid()
        => Assert.Empty(LdsSpecValidator.Validate(SampleRows.Remove() with { YearGroup = "12", RemovalYear0 = "TRUE", RemovalYear1 = "FALSE", RemovalYear2 = "" }));
}
