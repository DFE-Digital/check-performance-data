using Dfe.Analytics.AspNetCore;

namespace DfE.CheckPerformanceData.Web.Analytics;

/// <summary>
/// Stamps the signed-in user's organisation (school) URN and name onto every
/// <c>web_request</c> analytics event, satisfying the "all web requests: user_id,
/// OrgURN, OrgName" success measure. <c>user_id</c> is added by the middleware from
/// the <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/> claim, so it is
/// not duplicated here. Claims that are absent (e.g. on unauthenticated sign-in pages)
/// are omitted rather than stamped empty.
/// </summary>
public sealed class OrganisationEventEnricher : IWebRequestEventEnricher
{
    public Task EnrichEventAsync(EnrichWebRequestEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.HttpContext.User;
        AddClaim(context, user, "organisation_urn");
        AddClaim(context, user, "organisation_name");

        return Task.CompletedTask;
    }

    private static void AddClaim(
        EnrichWebRequestEventContext context, System.Security.Claims.ClaimsPrincipal user, string claimType)
    {
        var value = user.FindFirst(claimType)?.Value;
        if (!string.IsNullOrEmpty(value))
            context.Event.AddData(claimType, value);
    }
}
