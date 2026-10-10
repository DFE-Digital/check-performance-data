using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Infrastructure.Impersonation;
using DfE.CheckPerformanceData.Persistence.Locking;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Impersonation;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class ImpersonationExtensions
{
    public static IServiceCollection AddEstablishmentImpersonation(this IServiceCollection services)
    {
        services.AddScoped<ImpersonationSessionService>();
        services.AddScoped<IEstablishmentViewContext>(sp => sp.GetRequiredService<ImpersonationSessionService>());
        services.AddScoped<IImpersonationWriteGuard>(sp => sp.GetRequiredService<ImpersonationSessionService>());
        services.AddScoped<IImpersonationSessionStore, DistributedImpersonationSessionStore>();
        services.AddScoped<IImpersonationSessionLock, PostgresImpersonationSessionLock>();
        services.AddScoped<IImpersonationAuditWriter, ImpersonationAuditWriter>();
        return services;
    }
}
