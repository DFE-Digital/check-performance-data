namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class ContactViewModel
{
    public bool IsAuthenticated { get; set; }

    /// <summary>Safe-local return target, carried through the POST as a hidden field.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>The only validated field — a value from <see cref="ContactEnquiryTypes"/>.</summary>
    public string? EnquiryType { get; set; }

    // Signed-in: read-only establishment context from session claims.
    public string? OrganisationName { get; set; }
    public string? OrganisationLaestab { get; set; }

    // Anonymous: unvalidated, never persisted.
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? School { get; set; }

    // Both: unvalidated, never persisted.
    public string? Details { get; set; }

    // Presentation state.
    public bool HasHighlightContent { get; set; }
    public IReadOnlyList<ContactEnquiryType> EnquiryOptions { get; set; } = [];
}
