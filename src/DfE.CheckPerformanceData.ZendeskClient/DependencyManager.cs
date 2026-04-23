//using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Application;
using DfE.CheckPerformanceData.Application.ZendeskApi;
using DfE.CheckPerformanceData.ZendeskClient.Refit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Options.ConfigurationExtensions;
using Refit;
using Refit;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    /// <summary>
    /// Move this to the ingrasture project and paste in the method, or add dependencies here.
    /// </summary>
    public static class ZDDependencyManager
    {
        public static IServiceCollection AddZendeskApiClient(this IServiceCollection services, IConfiguration config)
        {
            // optional logging setup.
            services.AddTransient<RefitLoggingHandler>();
        
            var settings = config.GetSection(ZendeskSettings.SectionName).Get<ZendeskSettings>();

            if (settings == null)
            {
                throw new InvalidOperationException("ZendeskSettings section is missing in the configuration.");
            }
            services.Configure<ZendeskSettings>(s => s = settings);

            services.AddTransient<RefitLoggingHandler>();


            services.AddRefitClient<IZendeskApi>(new RefitSettings
            {
                ContentSerializer = new NewtonsoftJsonContentSerializer()
            })

               .ConfigureHttpClient(c =>
               {
                   c.BaseAddress = new Uri($"https://{settings.Subdomain}.{settings.Domain}.com");
                   var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Email}/token:{settings.ApiToken}"));
                   c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
                   c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
               })
               .AddHttpMessageHandler<RefitLoggingHandler>();

            return services;
        }
    }
}
