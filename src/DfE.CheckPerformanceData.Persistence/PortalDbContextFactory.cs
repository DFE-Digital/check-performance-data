using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DfE.CheckPerformanceData.Persistence;

/// <summary>
/// Design-time factory used exclusively by EF Core tooling (migrations add/update-database).
/// Not used at runtime. Bypasses the Web startup host so that migrations can be generated
/// without live configuration (DfeSignIn, Key Vault, etc.).
/// </summary>
internal sealed class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PortalDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=design-time-placeholder;Username=placeholder");

        return new PortalDbContext(optionsBuilder.Options, new NullCurrentUserService());
    }

    private sealed class NullCurrentUserService : ICurrentUserService
    {
        public string UserId => string.Empty;
        public string DisplayName => string.Empty;
        public string Email => string.Empty;
        public string OrganisationId => string.Empty;
        public string OrganisationName => string.Empty;
        public string OrganisationUrn => string.Empty;
        public string OrganisationLaestab => string.Empty;
        public string OrganisationTypeId => string.Empty;
    }
}
