using DfE.CheckPerformance.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformance.Persistence;

public static class DependencyManager
{
    public static IServiceCollection AddPersistenceDependencies(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IPortalDbContext, PortalDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Postgres");

            options
                .UseNpgsql(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure();
            })
                .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        return services;
    }
}
