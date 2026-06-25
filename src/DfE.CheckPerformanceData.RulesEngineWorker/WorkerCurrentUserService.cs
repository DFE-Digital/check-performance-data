using DfE.CheckPerformanceData.Application.CurrentUser;

namespace DfE.CheckPerformanceData.RulesEngineWorker;

/// <summary>
/// Identity used for audit entries the worker writes. The worker acts on behalf of
/// the system rather than an interactive user, so it reports a fixed system id.
/// <see cref="ICurrentUserService"/> is needed because <c>PortalDbContext</c> stamps
/// audit rows with a UserId on SaveChanges.
/// </summary>
public sealed class WorkerCurrentUserService : ICurrentUserService
{
    public string UserId => "rules-engine-worker";
    public string DisplayName => "Rules Engine Worker";
    public string Email => string.Empty;
    public string OrganisationId => string.Empty;
    public string OrganisationName => string.Empty;
    public string OrganisationUrn => string.Empty;
    public string OrganisationLaestab => string.Empty;
    public string OrganisationTypeId => string.Empty;
    public string Ukprn => string.Empty;
}
