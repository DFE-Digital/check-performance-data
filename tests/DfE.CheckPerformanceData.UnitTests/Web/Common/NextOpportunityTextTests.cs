using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Common;

// AB#298317: one spelling of the next-opportunity date for the landing banner, Check your pupil
// data and the admin Summary. Month + year only — the day is captured but never shown to schools.
public sealed class NextOpportunityTextTests
{
    [Fact]
    public void Formats_as_month_and_year()
    {
        Assert.Equal("October 2027", NextOpportunityText.For(new DateTime(2027, 10, 14)));
    }

    [Fact]
    public void Null_stays_null_so_callers_can_omit_the_sentence()
    {
        Assert.Null(NextOpportunityText.For(null));
    }
}
