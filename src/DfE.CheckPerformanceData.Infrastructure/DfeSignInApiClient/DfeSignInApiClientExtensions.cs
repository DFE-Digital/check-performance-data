using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public static class DfeSignInApiClientExtensions
{
    public static IServiceCollection AddDfeApiClient(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DfeSigninSettings>(config.GetSection(DfeSigninSettings.SectionName));
        
        services.AddHttpClient<IDfESignInApiClient, DfeSignInApiClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<DfeSigninSettings>>().Value;
        
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.ApiClientSecret));
            var descriptor = new SecurityTokenDescriptor()
            {
                Issuer = settings.ClientId,
                Audience = settings.Audience,
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };
        
            var token = tokenHandler.CreateEncodedJwt(descriptor);

            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        });
        
        return services;
    }
}