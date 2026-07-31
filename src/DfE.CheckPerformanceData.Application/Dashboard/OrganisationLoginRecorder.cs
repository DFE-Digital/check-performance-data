using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Dashboard;

public sealed class OrganisationLoginRecorder(
    IOrganisationLoginRepository repository,
    ILogger<OrganisationLoginRecorder> logger) : IOrganisationLoginRecorder
{
    public async Task RecordLoginAsync(
        string userId, ClaimsIdentity enrichedIdentity, CancellationToken cancellationToken = default)
    {
        var urnValue = enrichedIdentity.FindFirst("organisation_urn")?.Value;
        var laestab = LaestabNormaliser.Normalise(enrichedIdentity.FindFirst("organisation_laestab")?.Value);
        var name = enrichedIdentity.FindFirst("organisation_name")?.Value ?? string.Empty;

        if (!long.TryParse(urnValue, out var urn) || laestab.Length == 0)
        {
            // Some DfE Sign-In organisations (e.g. LAs) legitimately carry no laestab; those
            // logins are not school engagement, so skipping is correct rather than an error.
            logger.LogWarning(
                "Organisation login not recorded for user {UserId}: URN '{Urn}' or laestab missing/invalid.",
                userId, urnValue);
            return;
        }

        await repository.RecordAsync(
            new OrganisationLoginRecord(userId, urn, laestab, name), cancellationToken);
    }
}
