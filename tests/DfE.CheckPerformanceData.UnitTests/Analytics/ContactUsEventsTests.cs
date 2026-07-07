using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class ContactUsEventsTests
{
    [Fact]
    public void ContactUsSubmittedEvent_projects_enquiry_type_and_auth_flag()
    {
        var e = new ContactUsSubmittedEvent { EnquiryType = "technical-problem", IsAuthenticated = true };

        Assert.Equal("contact_us_submitted", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("technical-problem", byName["enquiry_type"].Value);
        Assert.Equal(true, byName["is_authenticated"].Value);
        Assert.False(byName["enquiry_type"].Hidden);
        Assert.False(byName["is_authenticated"].Hidden);
    }
}
