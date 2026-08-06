using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Application.UnitTests.ZendeskClient;

/// <summary>
/// Exercises the real <see cref="ZendeskTicketFieldOptions"/> option maps directly
/// (the ticket-composition tests stub <see cref="IZendeskTicketFieldService"/>,
/// so they cannot catch a missing option here).
/// </summary>
public sealed class ZendeskTicketFieldOptionsTests
{
    [Theory]
    [InlineData("KS2", "ks2")]
    [InlineData("KS4June", "ks4")]
    [InlineData("KS4Autumn", "ks4")]
    [InlineData("ks4", "ks4")]
    public void KeyStage_GetOptionValue_MapsCheckingWindowTypes(string windowType, string expected)
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, windowType);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void KeyStage_GetOptionValue_ReturnsNullForUnmappedWindowType()
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, "Post16");

        Assert.Null(value);
    }

    [Theory]
    [InlineData("Inclusion", "inclusion_criteria_met")]
    [InlineData("AdmittedFollowingPermanentExclusion", "admitted_following_permanent_exclusion_criteria_met")]
    [InlineData("AdmittedFromAbroadEal", "admitted_from_abroad_with_english_not_first_language_criteria_met")]
    [InlineData("CompletedKs4Elsewhere", "add_back_removal_criteria_met")]
    [InlineData("MergePupils", "merge_pupils_criteria_met")]
    [InlineData("SocialCareInvolvement", "social_care_involvement_including_police/prison_-_criteria_met")]
    [InlineData("TerminalCriticalIllness", "terminal/critical_illness_-_criteria_met")]
    [InlineData("YearGroupChange", "year_group_changed_to_year_10")]
    [InlineData("Deceased", "deceased_criteria_met")]
    [InlineData("ElectiveHomeEducation", "elective_home_education_criteria_met")]
    [InlineData("MovedSchoolDualRegistration", "moved_school_dual_registration_criteria_met")]
    [InlineData("NotOnRoll", "not_on_roll_criteria_met")]
    [InlineData("PermanentlyExcludedFromCurrentSchool", "permanently_excluded_from_current_school_criteria_met")]
    [InlineData("PermanentlyLeftEngland", "permanently_left_england_criteria_met")]
    [InlineData("PupilMissingInEducation", "pupil_missing_in_education_-_criteria_met")]
    [InlineData("AssessmentsDeferred", "one_or_more_end_of_key_stage_assessments_deferred_by_a_year_criteria_met")]
    [InlineData("PupilAddedAfterSummerTerm", "pupil_added_to_school_roll_after_start_of_summer_term_criteria_met")]
    [InlineData("PupilNotOnJuneList", "pupil_not_on_june_list_criteria_met")]
    [InlineData("NotAtEndOf16To18Study", "not_at_end_of_16_to_18_study_criteria_met")]
    public void DecisionReasonApproved_GetOptionValue_MapsEveryOutcomeKey(string outcomeKey, string expected)
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, outcomeKey);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void DecisionReasonApproved_GetOptionValue_ReturnsNullForUnmappedOutcome()
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, "Other");

        Assert.Null(value);
    }

    [Theory]
    [InlineData("permanent-exclusion", "13_31")]
    [InlineData("english-not-first-language", "2_31")]
    [InlineData("child-missing-education", "501_31")]
    [InlineData("pupil-died", "4_31")]
    [InlineData("dual-registered-moved", "332_31")]
    [InlineData("elective-home-education", "27_31")]
    [InlineData("not-on-roll", "6_31")]
    [InlineData("permanently-excluded", "500_31")]
    [InlineData("permanently-left-england", "3_31")]
    [InlineData("social-care-involvement", "502_31")]
    [InlineData("life-limiting-illness", "503_31")]
    [InlineData("year-group-change", "17_31")]
    public void CorrectionReason31_GetOptionValue_MapsEveryTopLevelRemovalReason(string removalReason, string expected)
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.CorrectionReason31Name, removalReason);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("completed-ks4-elsewhere")]
    [InlineData("other")]
    public void CorrectionReason31_GetOptionValue_ReturnsNullForAddBackRemovalReason(string removalReason)
    {
        var value = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.CorrectionReason31Name, removalReason);

        Assert.Null(value);
    }
}
