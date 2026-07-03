using System.Security.Claims;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public sealed class ClaimsEnrichmentService(
    IDfESignInApiClient apiClient,
    ILogger<ClaimsEnrichmentService> logger) : IClaimsEnrichmentService
{
    public async Task<ClaimsIdentity?> EnrichAsync(ClaimsPrincipal identity)
    {
        var userid = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userid == null)
        {
            logger.LogWarning(
                "Claims enrichment failed: the authenticated principal has no NameIdentifier (sub) claim. " +
                "User will be sent to the no-access page.");
            return null;
        }

        var orgJson = identity.FindFirst("organisation")?.Value;
        if (orgJson == null)
        {
            logger.LogWarning(
                "Claims enrichment failed for user {UserId}: DfE Sign-in returned no 'organisation' claim. " +
                "Check the 'organisationid' scope is granted and that the user selected an organisation at sign-in. " +
                "User will be sent to the no-access page.",
                userid);
            return null;
        }

        var org = JsonNode.Parse(orgJson);
        var orgId = org!["id"]?.ToString() ?? string.Empty;
        logger.LogInformation(
            "Enriching claims for user {UserId} against organisation {OrgId}.", userid, orgId);

        var roles = await apiClient.GetUserRolesAsync(orgId, userid);
        if (roles.Count == 0)
        {
            logger.LogWarning(
                "Claims enrichment failed for user {UserId}: DfE Sign-in returned no roles for organisation {OrgId} " +
                "against the service configured in DfeSignIn:ServiceId (a 404 from the access endpoint is treated as 'no roles'). " +
                "Verify the user has a role assigned for THIS service and that DfeSignIn:ServiceId is the production service id. " +
                "User will be sent to the no-access page.",
                userid, orgId);
            return null;
        }

        var roleClaims = roles.Select(r => new Claim(ClaimTypes.Role, r.Code));

        var organisation = await apiClient.GetOrganisationAsync(userid, orgId);
        if (organisation == null)
        {
            logger.LogWarning(
                "Claims enrichment failed for user {UserId}: organisation {OrgId} from the sign-in token could not be " +
                "matched to any organisation returned by the DfE Sign-in API for this user (a 404 from the organisations " +
                "endpoint, or an id/casing mismatch on the returned list). User will be sent to the no-access page.",
                userid, orgId);
            return null;
        }

        // The Claim constructor throws ArgumentNullException on a null value. Some DfE Sign-in
        // organisations leave these fields null (e.g. Laestab is only populated when the org
        // record carries a localAuthority element), so coalesce to empty rather than aborting
        // sign-in — downstream consumers already treat a missing claim as empty. A null laestab
        // means pupil data will not resolve, so surface it for investigation.
        if (string.IsNullOrEmpty(organisation.Laestab) || string.IsNullOrEmpty(organisation.Name))
            logger.LogWarning(
                "DfE Sign-in organisation {OrgId} (URN {Urn}) is missing fields during enrichment " +
                "(name present: {HasName}, laestab present: {HasLaestab}). Enrichment will continue with empty values.",
                orgId, organisation.Urn, !string.IsNullOrEmpty(organisation.Name), !string.IsNullOrEmpty(organisation.Laestab));

        var newIdentity = new ClaimsIdentity(roleClaims, "DfeSignIn");
        newIdentity.AddClaim(new Claim("organisation_id", orgId, ClaimValueTypes.String));
        if (organisation.Ukprn != null)
            newIdentity.AddClaim(new Claim("ukprn", organisation.Ukprn, ClaimValueTypes.String));
        newIdentity.AddClaim(new Claim("organisation_name", organisation.Name ?? string.Empty, ClaimValueTypes.String));
        newIdentity.AddClaim(new Claim("organisation_urn", organisation.Urn ?? string.Empty, ClaimValueTypes.String));
        newIdentity.AddClaim(new Claim("organisation_laestab", organisation.Laestab ?? string.Empty, ClaimValueTypes.String));
        if (organisation.Type?.Id is { } typeId)
            newIdentity.AddClaim(new Claim("organisation_type_id", typeId, ClaimValueTypes.String));

        logger.LogInformation(
            "Claims enrichment succeeded for user {UserId}, organisation {OrgId} ({OrgName}) with {RoleCount} role(s).",
            userid, orgId, organisation.Name, roles.Count);
        return newIdentity;
    }
}