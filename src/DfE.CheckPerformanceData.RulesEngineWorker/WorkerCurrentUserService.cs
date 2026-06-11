using DfE.CheckPerformanceData.Application.CurrentUser;

namespace DfE.CheckPerformanceData.RulesEngineWorker;

/// <summary>
/// The worker has no signed-in user; <see cref="ICurrentUserService"/> is needed
/// only because <c>PortalDbContext</c> stamps audit rows with a UserId on
/// SaveChanges. Writes made by the worker are attributed to this system identity.
/// </summary>
public sealed class WorkerCurrentUserService : ICurrentUserService
{
    public string UserId => "rules-engine-worker";
    public string DisplayName => "Rules Engine Worker";
    public string OrganisationId => string.Empty;
    public string OrganisationName => string.Empty;
    public string OrganisationUrn => string.Empty;
    public string OrganisationTypeId => string.Empty;
}
