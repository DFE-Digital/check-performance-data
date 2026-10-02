using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authentication;

namespace DfE.CheckPerformanceData.Web.Authentication;

// Pure mapping from cypd-dev-impersonation cookie value to an AuthenticationTicket.
// Split out from DevImpersonationAuthHandler so it's directly unit-testable without
// the IOptionsMonitor + ILoggerFactory plumbing the handler base class requires.
public static class DevImpersonationTicketBuilder
{
    private const string SyntheticNameIdentifier = "00000000-0000-0000-0000-000000000001";
    private const string SyntheticName = "Dev impersonation user";
    private const string SyntheticEmail = "dev-impersonation-user@education.gov.uk";

    // Synthetic org claims matching seeded dev data so the LandingPage does not
    // challenge through real DfE Sign-In. These match Kingsmead School (one of the
    // seeded schools in SeedPupilData.cs) so pupil data resolves correctly.
    private const string SyntheticOrganisationId = "mock-organisation-id";
    private const string SyntheticOrganisationName = "Kingsmead School";
    private const string SyntheticOrganisationUrn = "142313";
    private const string SyntheticOrganisationLaestab = "860/4070";
    private const string SyntheticOrganisationTypeId = "1";

    // GIAS establishment type "11" (Other Independent School). Journey conditions read this claim
    // through CurrentUserService, and the two independent-related removal reasons are gated on it
    // in opposite polarities — so a dev session that cannot express an independent school cannot
    // exercise either one. Only the IndependentUserValue cookie claims it.
    private const string IndependentOrganisationTypeId = "11";

    public static AuthenticationTicket? TryBuild(string cookieValue)
    {
        if (cookieValue != DevImpersonationConstants.EditorValue
            && cookieValue != DevImpersonationConstants.UserValue
            && cookieValue != DevImpersonationConstants.AdminValue
            && cookieValue != DevImpersonationConstants.IndependentUserValue)
        {
            return null;
        }

        var isIndependent = cookieValue == DevImpersonationConstants.IndependentUserValue;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, SyntheticNameIdentifier),
            new(ClaimTypes.Name, SyntheticName),
            new(ClaimTypes.Email, SyntheticEmail),
            new("organisation_id", SyntheticOrganisationId),
            new("organisation_name", SyntheticOrganisationName),
            new("organisation_urn", SyntheticOrganisationUrn),
            new("organisation_laestab", SyntheticOrganisationLaestab),
            new("organisation_type_id",
                isIndependent ? IndependentOrganisationTypeId : SyntheticOrganisationTypeId)
        };

        // Same role as EditorValue: this value varies the organisation type only, so it grants no
        // capability the editor value does not already grant.
        if (cookieValue == DevImpersonationConstants.EditorValue || isIndependent)
        {
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.EditorRole));
        }

        // Admin implies editor (one-way hierarchy). Stamping both role claims here
        // means every existing [Authorize(Roles = WikiConstants.EditorRole)] endpoint
        // automatically accepts an admin principal without any per-endpoint change.
        // The reverse is NOT true — an editor cookie never grants admin access.
        if (cookieValue == DevImpersonationConstants.AdminValue)
        {
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.AdminRole));
            claims.Add(new Claim(ClaimTypes.Role, WikiConstants.EditorRole));
        }

        var identity = new ClaimsIdentity(claims, DevImpersonationConstants.Scheme);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, DevImpersonationConstants.Scheme);
    }
}
