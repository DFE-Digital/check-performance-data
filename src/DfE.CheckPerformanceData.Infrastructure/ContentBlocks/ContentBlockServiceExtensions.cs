using DfE.CheckPerformanceData.Application.ContentBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Infrastructure.ContentBlocks;

public static class ContentBlockServiceExtensions
{
    public static IServiceCollection AddContentBlockService(this IServiceCollection services)
    {
        services.AddScoped<IContentBlockService, ContentBlockService>();
        return services;
    }
}
