namespace DfE.CheckPerformanceData.Web.Controllers;

/// <summary>Which users an enquiry type is offered to.</summary>
public enum EnquiryAudience { Both, SignedInOnly, AnonymousOnly }

/// <summary>One enquiry-type option. <see cref="Value"/> is a stable machine code (goes to
/// analytics/logs); <see cref="Label"/> is display-only.</summary>
public sealed record ContactEnquiryType(string Value, string Label, EnquiryAudience Audience);

/// <summary>
/// PLACEHOLDER enquiry-type catalogue for the Contact Us wayfinder. Hardcoded on purpose — the
/// channel has not been redesigned yet, so these values are expected to change via a cheap PR once
/// the real taxonomy is agreed. Signed-in users see Both + SignedInOnly; anonymous users see
/// Both + AnonymousOnly (a reduced set).
/// </summary>
public static class ContactEnquiryTypes
{
    public static readonly IReadOnlyList<ContactEnquiryType> All =
    [
        new("pupil-data-query", "Help with a pupil data query", EnquiryAudience.SignedInOnly),
        new("amendment-evidence", "Help with an amendment or evidence", EnquiryAudience.SignedInOnly),
        new("technical-problem", "Technical problem with the service", EnquiryAudience.Both),
        new("general-query", "General query", EnquiryAudience.AnonymousOnly),
        new("something-else", "Something else", EnquiryAudience.Both),
    ];

    public static IReadOnlyList<ContactEnquiryType> ForAudience(bool isAuthenticated)
    {
        var restricted = isAuthenticated ? EnquiryAudience.SignedInOnly : EnquiryAudience.AnonymousOnly;
        return All.Where(t => t.Audience == EnquiryAudience.Both || t.Audience == restricted).ToList();
    }

    public static bool IsValidFor(string? value, bool isAuthenticated) =>
        !string.IsNullOrEmpty(value) && ForAudience(isAuthenticated).Any(t => t.Value == value);
}
