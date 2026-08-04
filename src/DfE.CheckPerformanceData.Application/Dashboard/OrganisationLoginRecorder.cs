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
            // logins are not school engagement, so skipping is a normal path — Information,
            // not admin-surface noise. A URN that is present but not numeric is genuinely
            // malformed claim data and keeps the Warning.
            if (!string.IsNullOrEmpty(urnValue) && !long.TryParse(urnValue, out _))
            {
                logger.LogWarning(
                    "Organisation login not recorded for user {UserId}: URN '{Urn}' is not numeric.",
                    userId, urnValue);
            }
            else
            {
                logger.LogInformation(
                    "Organisation login not recorded for user {UserId}: organisation has no school laestab.",
                    userId);
            }
            return;
        }

        // userId is used only for the skip-log above — the row itself stores organisation
        // data only. The DfE Sign-In user id was dropped on data-minimisation grounds: no
        // feature reads it and the table has no retention limit (see docs/admin-dashboard.md).
        await repository.RecordAsync(
            new OrganisationLoginRecord(urn, laestab, name), cancellationToken);
    }
}
