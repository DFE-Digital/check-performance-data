using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class MissingQualificationDateRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 24);

    private static JourneyPage Page() => new()
    {
        Id = MissingQualificationDateRules.AwardDatePageId,
        Questions = [new Question { Id = MissingQualificationDateRules.AwardDateQuestionId,
            Type = QuestionType.Date, Title = "Provide award date" }]
    };

    private static Dictionary<string, QuestionAnswer> Answered(int d, int m, int y) => new()
    {
        [MissingQualificationDateRules.AwardDateQuestionId] =
            new QuestionAnswer { DateValue = new DateAnswer { Day = d, Month = m, Year = y } }
    };

    [Fact]
    public void A_future_award_date_is_rejected_with_the_tickets_copy()
    {
        var v = Assert.Single(MissingQualificationDateRules.Evaluate(Page(), Answered(25, 8, 2026), Today));
        Assert.Equal("Award date must be today or in the past", v.Message);
    }

    [Fact]
    public void Today_is_acceptable() =>
        Assert.Empty(MissingQualificationDateRules.Evaluate(Page(), Answered(24, 8, 2026), Today));

    [Fact]
    public void A_date_before_september_2023_is_outside_the_enquiry_window()
    {
        // AB#298201: enquiries cover the 2023/24 and 2024/25 academic years only.
        var v = Assert.Single(MissingQualificationDateRules.Evaluate(Page(), Answered(31, 8, 2023), Today));
        Assert.Equal(
            "We are only able to allow results enquiries for results awarded during the 2023/24 and 2024/25 academic years",
            v.Message);
    }

    [Fact]
    public void The_first_of_september_2023_is_inside_the_window() =>
        Assert.Empty(MissingQualificationDateRules.Evaluate(Page(), Answered(1, 9, 2023), Today));

    [Fact]
    public void A_blank_or_impossible_date_is_left_to_the_format_rules()
    {
        // Only the first error per question renders; a second message here would replace
        // "must be a real date" with a comparison against a date the user never entered.
        Assert.Empty(MissingQualificationDateRules.Evaluate(Page(), new Dictionary<string, QuestionAnswer>(), Today));
        Assert.Empty(MissingQualificationDateRules.Evaluate(Page(), Answered(31, 2, 2024), Today));
    }
}
