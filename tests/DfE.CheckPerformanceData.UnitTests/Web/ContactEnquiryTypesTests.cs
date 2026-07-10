using DfE.CheckPerformanceData.Web.Controllers;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ContactEnquiryTypesTests
{
    [Fact]
    public void ForAudience_signed_in_returns_both_and_signed_in_only_in_order()
    {
        var values = ContactEnquiryTypes.ForAudience(isAuthenticated: true).Select(t => t.Value).ToArray();
        Assert.Equal(new[] { "pupil-data-query", "amendment-evidence", "technical-problem", "something-else" }, values);
    }

    [Fact]
    public void ForAudience_anonymous_returns_both_and_anonymous_only_in_order()
    {
        var values = ContactEnquiryTypes.ForAudience(isAuthenticated: false).Select(t => t.Value).ToArray();
        Assert.Equal(new[] { "technical-problem", "general-query", "something-else" }, values);
    }

    [Fact]
    public void IsValidFor_rejects_signed_in_only_value_for_anonymous()
    {
        Assert.False(ContactEnquiryTypes.IsValidFor("pupil-data-query", isAuthenticated: false));
        Assert.True(ContactEnquiryTypes.IsValidFor("pupil-data-query", isAuthenticated: true));
    }

    [Fact]
    public void IsValidFor_rejects_null_empty_and_unknown()
    {
        Assert.False(ContactEnquiryTypes.IsValidFor(null, isAuthenticated: true));
        Assert.False(ContactEnquiryTypes.IsValidFor("", isAuthenticated: true));
        Assert.False(ContactEnquiryTypes.IsValidFor("not-a-real-code", isAuthenticated: true));
    }
}
