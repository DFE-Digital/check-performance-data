using DfE.CheckPerformanceData.Application.Wiki;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Infrastructure.Wiki;

public static class WikiServiceExtensions
{
    public static IServiceCollection AddWikiService(this IServiceCollection services)
    {
        services.AddScoped<IWikiService, WikiService>();
        return services;
    }
}
